using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Создаёт контейнеры только для видимых элементов. Требует одинаковой
/// высоты строк — иначе нельзя вычислить видимый диапазон без измерения всех.
/// </summary>
public class VirtualizingStackPanel : DecoratedPanel
{
    private readonly Dictionary<int, UIElement> _realized = [];
    private readonly Stack<UIElement> _recycled = new();

    private int _firstVisible;
    private int _visibleCount;

    public IList<object> ItemsSource { get; set; } = [];

    public Func<object, UIElement>? ItemTemplate { get; set; }

    /// <summary>Высота строки. Одинакова для всех — на этом строится виртуализация.</summary>
    public float ItemHeight { get; set; } = 24f;

    /// <summary>Сколько строк готовить сверх видимых, чтобы прокрутка не мигала.</summary>
    public int OverscanCount { get; set; } = 3;

    public VirtualizingStackPanel()
    {
        OverflowY = Overflow.Auto;
    }

    public void Refresh()
    {
        RecycleAll();
        Invalidate();
    }

    private void RecycleAll()
    {
        while (Children.Count > 0)
            Children.RemoveAt(Children.Count - 1);

        foreach (UIElement container in _realized.Values)
            _recycled.Push(container);

        _realized.Clear();
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

    private void UpdateRealizedRange(float viewportHeight)
    {
        if (ItemsSource.Count == 0 || ItemHeight <= 0)
        {
            RecycleAll();
            return;
        }

        int first = Math.Max(0, (int)(ScrollY / ItemHeight) - OverscanCount);
        int count = (int)Math.Ceiling(viewportHeight / ItemHeight) + OverscanCount * 2;
        count = Math.Min(count, ItemsSource.Count - first);

        if (first == _firstVisible && count == _visibleCount && _realized.Count > 0)
            return;

        _firstVisible = first;
        _visibleCount = count;

        // убираем то, что вышло за окно, в переиспользование
        List<int> stale = [];

        foreach (int index in _realized.Keys)
            if (index < first || index >= first + count)
                stale.Add(index);

        foreach (int index in stale)
        {
            UIElement container = _realized[index];
            Children.Remove(container);
            _realized.Remove(index);
            _recycled.Push(container);
        }

        for (int i = first; i < first + count; i++)
        {
            if (_realized.ContainsKey(i)) continue;

            // шаблон может не подойти переиспользованному контейнеру,
            // поэтому пул работает, только когда шаблон не задан
            UIElement container = ItemTemplate is null && _recycled.Count > 0
                ? Reuse(_recycled.Pop(), ItemsSource[i])
                : CreateContainer(ItemsSource[i]);

            _realized[i] = container;
            Children.Add(container);
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
        float viewportHeight = float.IsFinite(availableSize.Height)
            ? availableSize.Height
            : ItemHeight * 20;

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

        foreach ((int index, UIElement container) in _realized)
        {
            container.Arrange(new Rectangle(
                new Point(Padding.Left, Padding.Top + index * ItemHeight),
                new Size(width, ItemHeight)));
        }
    }
}