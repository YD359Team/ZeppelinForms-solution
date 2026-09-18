using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Создаёт контейнеры только для видимых элементов. Требует одинаковой
/// высоты строк — иначе нельзя вычислить видимый диапазон без измерения всех.
/// </summary>
/// <remarks>
/// Контейнер принадлежит элементу, а не позиции в списке. Когда состав
/// источника меняется — узел дерева раскрылся, строку вставили выше
/// видимого окна, — уже созданные контейнеры не пересоздаются, а просто
/// переезжают на новый индекс. Пересоздание по индексу отвязывало всё окно
/// строк от дерева и строило его заново: терялись наведение и нажатие,
/// заново применялась тема, а строка, по которой кликнули, исчезала прямо
/// внутри своего OnClick.
/// </remarks>
public class VirtualizingStackPanel : DecoratedPanel
{
    private Dictionary<int, UIElement> _realized = [];
    private Dictionary<int, UIElement> _next = [];
    private readonly Stack<UIElement> _recycled = new();

    // элемент источника, который показывает контейнер. Сравнение по ссылке:
    // узел дерева с переопределённым Equals не должен забрать чужую строку
    private readonly Dictionary<UIElement, object?> _itemOf = new(ReferenceEqualityComparer.Instance);

    // рабочие коллекции пересборки диапазона — поля, а не локальные,
    // чтобы прокрутка не выделяла память на каждую строку
    private readonly Dictionary<object, UIElement> _byItem = new(ReferenceEqualityComparer.Instance);
    private readonly List<UIElement> _unmatched = [];
    private readonly List<int> _missing = [];

    private int _firstVisible;
    private int _visibleCount;
    private bool _rangeValid;

    public IList<object> ItemsSource { get; set; } = [];

    public Func<object, UIElement>? ItemTemplate
    {
        get;
        set
        {
            if (field == value) return;

            // контейнеры прежнего шаблона новому не подходят, а пул
            // подписей бесполезен шаблону — сбрасываем и то, и другое
            RecycleAll();
            _recycled.Clear();

            field = value;
            _rangeValid = false;

            Invalidate();
        }
    }

    /// <summary>Высота строки. Одинакова для всех — на этом строится виртуализация.</summary>
    public float ItemHeight
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // от высоты строки зависит и общий размер, и видимый диапазон
            _rangeValid = false;
            Invalidate();
        }
    } = 24f;

    /// <summary>Сколько строк готовить сверх видимых, чтобы прокрутка не мигала.</summary>
    public int OverscanCount
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            _rangeValid = false;
            Invalidate();
        }
    } = 3;

    public VirtualizingStackPanel()
    {
        OverflowY = Overflow.Auto;
    }

    /// <summary>Состав или порядок ItemsSource изменились.</summary>
    public void Refresh()
    {
        // детей здесь не трогаем: вычистить их сейчас значит показать пустой
        // список в кадре между Refresh и ближайшим измерением — это и есть
        // мерцание. Достаточно объявить диапазон недействительным,
        // а пересоберёт его UpdateRealizedRange в том же проходе раскладки
        _rangeValid = false;

        Invalidate();
    }

    private void RecycleAll()
    {
        SuppressChildrenInvalidate++;

        try
        {
            foreach (UIElement container in _realized.Values)
            {
                Children.Remove(container);
                _itemOf.Remove(container);
                Recycle(container);
            }

            _realized.Clear();
        }
        finally
        {
            SuppressChildrenInvalidate--;
        }
    }

    /// <summary>Вернуть контейнер в пул — или выбросить, если пул бесполезен.
    /// Собственный шаблон переиспользованному контейнеру не подходит
    /// (Reuse умеет только Label), поэтому такие контейнеры никогда
    /// не достаются обратно, и складывать их значит копить мусор до
    /// закрытия окна.</summary>
    private void Recycle(UIElement container)
    {
        if (ItemTemplate is not null) return;

        _recycled.Push(container);
    }

    private UIElement CreateContainer(object item) =>
        ItemTemplate?.Invoke(item) ?? new Label
        {
            Text = item?.ToString() ?? string.Empty,
            TextColor = Colors.Black,
            HorizontalContentAlign = HorizontalContentAlignment.Left,
            VerticalContentAlign = VerticalContentAlignment.Center,
            Padding = new Thickness(6, 3),
        };

    /// <summary>Привести набор контейнеров к видимому окну.</summary>
    /// <returns>true, если состав контейнеров изменился и их надо измерить.</returns>
    private bool UpdateRealizedRange(float viewportHeight)
    {
        if (ItemsSource.Count == 0 || ItemHeight <= 0)
        {
            bool hadContainers = _realized.Count > 0;

            RecycleAll();
            _rangeValid = false;

            return hadContainers;
        }

        int total = ItemsSource.Count;

        // ScrollY здесь может быть от прошлого состава: список только что
        // свернулся, а зажмёт прокрутку PanelControl лишь в размещении.
        // Без зажима first уезжал за конец, и count уходил в минус
        int first = Math.Clamp((int)(ScrollY / ItemHeight) - OverscanCount, 0, total - 1);
        int count = (int)Math.Ceiling(viewportHeight / ItemHeight) + OverscanCount * 2;
        count = Math.Clamp(count, 0, total - first);

        // ранний выход обязан смотреть на _rangeValid: после Refresh диапазон
        // часто прежний, а элементы в нём уже другие. Без этой проверки
        // раскрытие узла оставляло строки от старой проекции
        if (_rangeValid && first == _firstVisible && count == _visibleCount)
            return false;

        SuppressChildrenInvalidate++;

        try
        {
            // 1. прежние контейнеры по их элементам. Повтор той же ссылки
            // в источнике (одна строка дважды) получает отдельный контейнер
            foreach (UIElement container in _realized.Values)
            {
                object? item = _itemOf.GetValueOrDefault(container);

                if (item is null || !_byItem.TryAdd(item, container))
                    _unmatched.Add(container);
            }

            // 2. новое окно: всё, что было видно, остаётся тем же объектом
            // и лишь переезжает на свой новый индекс
            for (int i = first; i < first + count; i++)
            {
                object item = ItemsSource[i];

                if (item is not null && _byItem.Remove(item, out UIElement? kept))
                    _next[i] = kept;
                else
                    _missing.Add(i);
            }

            // всё, что в новое окно не попало, свободно
            _unmatched.AddRange(_byItem.Values);
            _byItem.Clear();

            // 3. недостающие строки. Без шаблона свободную подпись
            // перепривязываем прямо на месте — отвязывать её от дерева,
            // чтобы тут же привязать обратно, незачем
            int free = 0;

            foreach (int index in _missing)
            {
                object item = ItemsSource[index];
                UIElement container;

                // шаблон может не подойти переиспользованному контейнеру,
                // поэтому пул работает, только когда шаблон не задан
                if (ItemTemplate is null && free < _unmatched.Count)
                {
                    container = Reuse(_unmatched[free++], item);
                }
                else if (ItemTemplate is null && _recycled.Count > 0)
                {
                    container = Reuse(_recycled.Pop(), item);
                    Children.Add(container);
                }
                else
                {
                    container = CreateContainer(item);
                    Children.Add(container);
                }

                _itemOf[container] = item;
                _next[index] = container;
            }

            // 4. убираем то, что вышло за окно, в переиспользование
            for (; free < _unmatched.Count; free++)
            {
                UIElement container = _unmatched[free];

                Children.Remove(container);
                _itemOf.Remove(container);
                Recycle(container);
            }

            (_realized, _next) = (_next, _realized);
            _next.Clear();

            _rangeValid = true;
            _firstVisible = first;
            _visibleCount = count;

            return true;
        }
        finally
        {
            _unmatched.Clear();
            _missing.Clear();

            SuppressChildrenInvalidate--;
        }
    }

    private static UIElement Reuse(UIElement container, object item)
    {
        if (container is Label label)
            label.Text = item?.ToString() ?? string.Empty;

        return container;
    }

    protected override Size MeasureContentOverride(Size availableSize)
    {
        // по прокручиваемой оси PanelControl даёт бесконечность, и настоящая
        // высота окна известна только по прошлому размещению. Двадцать строк —
        // лишь догадка для самого первого прохода: размещение её поправит
        float viewportHeight = float.IsFinite(availableSize.Height)
            ? availableSize.Height
            : ArrangedViewport.Height > 0 ? ArrangedViewport.Height : ItemHeight * 20;

        UpdateRealizedRange(viewportHeight);

        var itemSize = new Size(
            Math.Max(0, availableSize.Width - Padding.Horizontal),
            ItemHeight);

        float maxWidth = 0;

        foreach (UIElement container in _realized.Values)
        {
            container.Measure(itemSize);
            maxWidth = Math.Max(maxWidth, container.DesiredSize.Width);
        }

        // высота считается по всему списку, а не по созданным строкам —
        // иначе полоса прокрутки будет врать
        return new Size(
            maxWidth + Padding.Horizontal,
            ItemsSource.Count * ItemHeight + Padding.Vertical);
    }

    protected override void ArrangeContentOverride(Size contentSize)
    {
        float width = Math.Max(0, contentSize.Width - Padding.Horizontal);

        // здесь высота окна уже точная. Если догадка измерения не совпала —
        // окно выросло, панель впервые размещается, — досоздаём строки сразу,
        // а не просим второй проход: контейнеры тут же и меряются.
        // Ширина панели по авторазмеру при этом не пересчитывается до
        // следующего прохода — строки одной высоты её почти не меняют
        if (UpdateRealizedRange(ArrangedViewport.Height))
        {
            var itemSize = new Size(width, ItemHeight);

            foreach (UIElement container in _realized.Values)
                container.Measure(itemSize);
        }

        foreach ((int index, UIElement container) in _realized)
        {
            container.Arrange(new Rectangle(
                new Point(Padding.Left, Padding.Top + index * ItemHeight),
                new Size(width, ItemHeight)));
        }
    }
}