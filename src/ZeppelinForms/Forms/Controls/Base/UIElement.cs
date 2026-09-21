using System.Diagnostics;
using System.Reflection;
using ZeppelinForms.Animation;
using ZeppelinForms.Core.Text;
using ZeppelinForms.Data;
using ZeppelinForms.Diagnostics;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Effects;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.DragDrop;
using ZeppelinForms.Input.Gestures;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>
/// Base element of any UI tree node
/// </summary>
public abstract partial class UIElement : IGridPlaceable, IBorderedElement
{
    // ===== события =====
    public event EventHandler<PointerEventArgs>? PointerDown;
    public event EventHandler<PointerEventArgs>? PointerMove;
    public event EventHandler<PointerEventArgs>? PointerUp;
    public event EventHandler<PointerCancelEventArgs>? PointerCanceled;
    /// <summary>Элемент присоединён к форме. Нужно тем, кто не может
    /// работать без неё: анимации, подписки на жизненный цикл окна.</summary>
    public event EventHandler? Attached;
    public event EventHandler? Detached;
    public event EventHandler<char>? TextInput;
    public event EventHandler? GotFocus;
    public event EventHandler? LostFocus;
    public event EventHandler<DragDropEventArgs>? DragEnter;
    public event EventHandler<DragDropEventArgs>? DragOver;
    public event EventHandler? DragLeave;
    public event EventHandler<DragDropEventArgs>? Drop;
    public event EventHandler<MouseClickEventArgs>? Click;
    public event EventHandler<MouseClickEventArgs>? DoubleClick;
    public event EventHandler<MouseClickEventArgs>? RightClick;
    public event EventHandler<MouseClickEventArgs>? MiddleClick;
    public event EventHandler<MouseButtonEventArgs>? MouseDown;
    public event EventHandler<MouseButtonEventArgs>? MouseUp;
    public event EventHandler<MouseMoveEventArgs>? MouseMove;
    public event EventHandler<MouseMoveEventArgs>? MouseEnter;
    public event EventHandler<MouseMoveEventArgs>? MouseExit;
    public event EventHandler<MouseWheelEventArgs>? MouseWheel;
    public event EventHandler<KeyEventArgs>? PreviewKeyDown;
    public event EventHandler<KeyEventArgs>? KeyDown;
    public event EventHandler<KeyEventArgs>? KeyUp;

    //

    /// <summary>Биндинги по индексу свойства. Словарь создаётся при первой
    /// привязке: у большинства элементов биндингов нет вовсе.</summary>
    private Dictionary<int, Binding>? _bindings;

    private List<GestureRecognizer>? _gestures;

    /// <summary>Распознаватели жестов этого элемента. Список создаётся
    /// по первому обращению: у подавляющего большинства элементов
    /// жестов нет, и пустой список на каждый из них — это мегабайты
    /// на дереве в тысячу узлов.</summary>
    public IList<GestureRecognizer> GestureRecognizers => _gestures ??= [];

    /// <summary>Горячий путь конвейера: спрашивается на каждое нажатие
    /// и не должен ничего создавать.</summary>
    internal IReadOnlyList<GestureRecognizer>? GestureRecognizersOrNull => _gestures;

    public T AddGesture<T>(T recognizer) where T : GestureRecognizer
    {
        recognizer.Element = this;
        GestureRecognizers.Add(recognizer);

        return recognizer;
    }

    /// <summary>Свойство под управлением биндинга. Третий источник значения
    /// между темой и явным присваиванием: тема биндинг не перебивает,
    /// а присваивание из кода — перебивает и разрывает его.</summary>
    private ulong[]? _bound;

    public bool IsBound(StyledProperty property) => GetBit(_bound, property.Index);

    /// <summary>Привязать свойство к свойству источника.</summary>
    /// <remarks>Элемент держит биндинг, биндинг держит источник, а на элемент
    /// смотрит слабо — поэтому живая модель не удерживает закрытое окно.</remarks>
    public Binding Bind<T>(
        StyledProperty<T> property,
        object source,
        string sourcePropertyName,
        BindingMode mode = BindingMode.OneWay)
    {
        ArgumentNullException.ThrowIfNull(source);

        PropertyInfo sourceProperty = source.GetType().GetProperty(sourcePropertyName)
            ?? throw new ArgumentException(
                $"У {source.GetType().Name} нет свойства '{sourcePropertyName}'.",
                nameof(sourcePropertyName));

        // повторная привязка того же свойства заменяет прежнюю
        Unbind(property);

        var binding = new Binding(this, property, source, sourceProperty, mode);

        _bindings ??= [];
        _bindings[property.Index] = binding;

        SetBit(ref _bound, property.Index);

        binding.PushToTarget();

        return binding;
    }

    /// <summary>Разорвать привязку свойства.</summary>
    public void Unbind(StyledProperty property)
    {
        if (_bindings is null || !_bindings.Remove(property.Index, out Binding? binding))
            return;

        binding.Detach();
        ClearBit(_bound, property.Index);
    }

    /// <summary>Разорвать все привязки элемента.</summary>
    public void UnbindAll()
    {
        if (_bindings is null) return;

        foreach (Binding binding in _bindings.Values)
            binding.Detach();

        _bindings.Clear();
        _bound = null;
    }

    /// <summary>Запись из биндинга. Обходит проверку на явное присваивание,
    /// но само явным не считается — иначе первая же запись из источника
    /// закрыла бы свойство от последующих.</summary>
    internal void SetBoundValue(StyledProperty property, object? value)
    {
        property.WriteBoxedDirect(this, value);

        SetBit(ref _assigned, property.Index);

        if (property.AffectsLayout) Invalidate();
        else InvalidateVisual();

        OnStyledPropertyChanged(property);
    }

    // ===
    [Styled(Category = "Appearance")]
    public partial Color Background { get; set; }
    private static Color BackgroundDefault => Colors.Transparent;

    /// <summary>Градиентная заливка фона. Меньше двух точек — рисуется
    /// обычный <see cref="Background"/>.
    /// Список считается неизменяемым: чтобы поменять градиент, присвойте
    /// новый, а не правьте существующий — иначе перерисовки не будет.</summary>
    [Styled(Category = "Appearance")]
    public partial IReadOnlyList<GradientStop>? BackgroundGradient { get; set; }

    /// <summary>Направление градиента в градусах: 0 — слева направо.</summary>
    [Styled(Category = "Appearance")]
    public partial float BackgroundGradientAngle { get; set; }

    /// <summary>Есть ли что заливать градиентом.</summary>
    protected bool HasBackgroundGradient => BackgroundGradient is { Count: >= 2 };

    /// <summary>Залить фон: градиентом, если он задан, иначе сплошным цветом.
    /// Общий код для всех трёх Decorated*-баз.</summary>
    protected void FillBackground(Graphics g, Rectangle bounds)
    {
        if (HasBackgroundGradient)
        {
            g.FillGradient(bounds, CornerRadius, [.. BackgroundGradient!], BackgroundGradientAngle);
            return;
        }

        if (CurrentBackground.A > 0)
            g.FillRoundRectangle(bounds, CornerRadius, CurrentBackground);
    }

    /// <summary>Задать градиент из отдельных точек.</summary>
    public void SetBackgroundGradient(params GradientStop[] stops) => BackgroundGradient = stops;

    /// <summary>Ровный переход между двумя цветами.</summary>
    public void SetBackgroundGradient(Color from, Color to) =>
        BackgroundGradient = [new GradientStop(from, 0f), new GradientStop(to, 1f)];

    /// <summary>Цвет текста. Наследуется вниз: задайте его на панели —
    /// и все вложенные подписи, кнопки и поля подхватят.</summary>
    [Styled(Category = "Text", Inherits = true)]
    public partial Color TextColor { get; set; }
    private static Color TextColorDefault => Colors.Black;

    [Styled(Category = "Appearance")]
    public partial Color BorderColor { get; set; }
    private static Color BorderColorDefault => Colors.Transparent;

    [Styled(Category = "Appearance")]
    public partial float BorderWidth { get; set; }
    // ===

    [Styled(Category = "Layout", AffectsLayout = true)]
    public partial float FlexGrow { get; set; }

    [Styled(Category = "Layout", AffectsLayout = true)]
    public partial Dock Docking { get; set; }

    [Styled(Category = "Layout", AffectsLayout = true)]
    public partial HorizontalAlignment HorizontalAlignment { get; set; }

    private static HorizontalAlignment HorizontalAlignmentDefault => HorizontalAlignment.Stretch;

    [Styled(Category = "Layout", AffectsLayout = true)]
    public partial VerticalAlignment VerticalAlignment { get; set; }

    private static VerticalAlignment VerticalAlignmentDefault => VerticalAlignment.Stretch;

    [Styled(Category = "Layout", AffectsLayout = true)]
    public partial FlowDirection? FlowDirection { get; set; }

    [Styled(Category = "Layout", AffectsLayout = true)]
    public partial Thickness Margin { get; set; }

    private static Thickness MarginDefault => Thickness.Zero;

    [Styled(Category = "Layout", AffectsLayout = true)]
    public partial Thickness Padding { get; set; }
    private static Thickness PaddingDefault => Thickness.Zero;

    [Styled(Category = "Grid", AffectsLayout = true)]
    public partial int Row { get; set; }

    [Styled(Category = "Grid", AffectsLayout = true)]
    public partial int Column { get; set; }

    [Styled(Category = "Grid", AffectsLayout = true)]
    public partial int RowSpan { get; set; }

    private static int RowSpanDefault => 1;

    [Styled(Category = "Grid", AffectsLayout = true)]
    public partial int ColumnSpan { get; set; }

    private static int ColumnSpanDefault => 1;

    [Styled(Category = "Layout", AffectsLayout = true)]
    public partial bool IsVisible { get; set; }

    private static bool IsVisibleDefault => true;

    /// <summary>Виден ли элемент с учётом предков.</summary>
    /// <remarks>
    /// Собственного IsVisible мало: PageControl прячет страницы, не отвязывая
    /// их от дерева, поэтому у элемента на скрытой странице IsVisible
    /// остаётся истинным, а на экране его нет.
    /// </remarks>
    public bool IsEffectivelyVisible
    {
        get
        {
            for (UIElement? node = this; node is not null; node = node.Parent)
                if (!node.IsVisible) return false;

            return true;
        }
    }

    [Styled(Category = "Text", AffectsLayout = true)]
    public partial Font? Font { get; set; }
    //
    /// <summary>Поворот в градусах вокруг центра элемента.</summary>
    [Styled(Category = "Appearance")]
    public partial float Rotation { get; set; }

    /// <summary>Сдвиг при отрисовке, не трогающий раскладку.</summary>
    /// <remarks>
    /// Место элемента остаётся тем, которое посчитала раскладка: соседи
    /// не разъезжаются, размеры не пересчитываются, попадание следует
    /// за картинкой. Именно поэтому сдвиг и масштаб годятся для переходов,
    /// а Margin или Size — нет: те меняют раскладку всего вокруг.
    /// </remarks>
    [Styled(Category = "Appearance")]
    public partial float TranslateX { get; set; }

    [Styled(Category = "Appearance")]
    public partial float TranslateY { get; set; }

    /// <summary>Масштаб при отрисовке вокруг центра элемента.</summary>
    [Styled(Category = "Appearance")]
    public partial float ScaleX { get; set; }
    private static float ScaleXDefault => 1f;

    [Styled(Category = "Appearance")]
    public partial float ScaleY { get; set; }
    private static float ScaleYDefault => 1f;

    [Styled(Category = "Behavior")]
    public partial bool IsEnabled { get; set; }

    private static bool IsEnabledDefault => true;

    [Styled(Category = "Appearance")]
    public partial float DisabledOpacity { get; set; }

    private static float DisabledOpacityDefault => 0.5f;

    [Styled(Category = "Appearance")]
    public partial float DisabledDesaturation { get; set; }

    private static float DisabledDesaturationDefault => 0.6f;

    [Styled(Category = "Appearance")]
    public partial BoxShadow? BoxShadow { get; set; }

    // ===== хуки для наследников =====
    /// <summary>Перетаскивание вошло в границы элемента.
    /// Здесь обычно включают подсветку и выставляют Effect.</summary>
    protected virtual void OnDragEnter(DragDropEventArgs e) { }

    /// <summary>Курсор двигается внутри элемента. Вызывается часто,
    /// поэтому тяжёлую работу здесь делать не стоит.</summary>
    protected virtual void OnDragOver(DragDropEventArgs e) { }

    /// <summary>Перетаскивание ушло или было отменено. Вызывается всегда,
    /// если был DragEnter, — в том числе когда бросок не состоялся.</summary>
    protected virtual void OnDragLeave() { }
    protected virtual void OnDrop(DragDropEventArgs e) { }
    protected virtual void OnMouseEnter(MouseMoveEventArgs e) { }
    protected virtual void OnMouseExit(MouseMoveEventArgs e) { }
    protected virtual void OnMouseMove(MouseMoveEventArgs e) { }
    protected virtual void OnMouseDown(MouseButtonEventArgs e) { }
    protected virtual void OnMouseUp(MouseButtonEventArgs e) { }
    protected virtual void OnClick(MouseClickEventArgs e) { }
    protected virtual void OnDoubleClick(MouseClickEventArgs e) { }
    protected virtual void OnRightClick(MouseClickEventArgs e) { }
    protected virtual void OnMiddleClick(MouseClickEventArgs e) { }
    protected virtual void OnMouseWheel(MouseWheelEventArgs e) { }
    /// <summary>Нажатие ещё до того, как оно дойдёт до попавшего элемента.
    /// Вызывается от корня к нему, поэтому предок может вмешаться раньше.</summary>
    protected virtual void OnPreviewMouseDown(MouseButtonEventArgs e) { }
    protected virtual void OnPreviewKeyDown(KeyEventArgs e) { }
    /// <summary>Отпускание до того, как оно дойдёт до нажатого элемента.
    /// Нужно предкам, которые следили за нажатием через предпросмотр.</summary>
    protected virtual void OnPreviewMouseUp(MouseButtonEventArgs e) { }
    /// <summary>Контакт коснулся элемента. Идёт от корня к цели — до того,
    /// как поднимутся совместимые события мыши. Сюда встраиваются
    /// распознаватели жестов: предку нужно увидеть контакт раньше потомка,
    /// иначе арбитраж невозможен.</summary>
    protected virtual void OnPointerDown(PointerEventArgs e) { }

    /// <summary>Контакт сдвинулся. Порядок тот же — от корня к цели.</summary>
    protected virtual void OnPointerMove(PointerEventArgs e) { }

    /// <summary>Контакт отпущен.</summary>
    protected virtual void OnPointerUp(PointerEventArgs e) { }

    /// <summary>Взаимодействие оборвано: завершения не будет, и всё, что
    /// начали по нажатию, надо откатить, а не зафиксировать.</summary>
    protected virtual void OnPointerCanceled(PointerCancelEventArgs e) { }
    protected virtual void OnKeyUp(KeyEventArgs e) { }
    protected virtual void OnTextInput(char c) { }
    /// <summary>Движение мыши до того, как оно дойдёт до попавшего элемента.
    /// Вызывается от корня к нему — предок может следить за перетаскиванием
    /// над своими потомками, не перехватывая ни попадание, ни захват.</summary>
    protected virtual void OnPreviewMouseMove(MouseMoveEventArgs e) { }

    // ===== подъём событий =====
    internal void RaiseAttached()
    {
        OnAttached();
        Attached?.Invoke(this, EventArgs.Empty);
    }

    internal void RaiseDragEnter(DragDropEventArgs e)
    {
        OnDragEnter(e);
        DragEnter?.Invoke(this, e);
    }

    internal void RaiseDragOver(DragDropEventArgs e)
    {
        OnDragOver(e);
        DragOver?.Invoke(this, e);
    }

    internal void RaiseDragLeave()
    {
        OnDragLeave();
        DragLeave?.Invoke(this, EventArgs.Empty);
    }

    internal void RaiseDrop(DragDropEventArgs e)
    {
        OnDrop(e);
        Drop?.Invoke(this, e);
    }

    internal void RaisePreviewMouseMove(MouseMoveEventArgs e) => OnPreviewMouseMove(e);

    internal void RaisePreviewMouseUp(MouseButtonEventArgs e) => OnPreviewMouseUp(e);

    internal void RaiseTextInput(char c)
    {
        OnTextInput(c);
        TextInput?.Invoke(this, c);
    }

    internal void RaiseGotFocus()
    {
        OnGotFocus();
        GotFocus?.Invoke(this, EventArgs.Empty);
    }

    internal void RaiseLostFocus()
    {
        OnLostFocus();
        LostFocus?.Invoke(this, EventArgs.Empty);
    }

    internal void RaiseMouseEnter(Point location, UIElement? from)
    {
        if (IsHovered) return;

        IsHovered = true;

        var args = new MouseMoveEventArgs(location) { RelatedElement = from };

        OnMouseEnter(args);
        MouseEnter?.Invoke(this, args);

        InvalidateVisual();
    }

    internal void RaiseMouseExit(Point location, UIElement? to)
    {
        if (!IsHovered) return;

        IsHovered = false;

        var args = new MouseMoveEventArgs(location) { RelatedElement = to };

        OnMouseExit(args);
        MouseExit?.Invoke(this, args);

        InvalidateVisual();
    }

    internal void RaiseMouseMove(Point location)
    {
        var args = new MouseMoveEventArgs(location);

        OnMouseMove(args);
        MouseMove?.Invoke(this, args);
    }

    internal void RaiseMouseDown(MouseButtonEventArgs e)
    {
        // прижатым считаем только левую: правая и средняя не «удерживают» контрол
        if (e.Button == MouseButton.Left)
            IsPressed = true;

        OnMouseDown(e);
        MouseDown?.Invoke(this, e);

        InvalidateVisual();
    }

    internal void RaiseMouseUp(MouseButtonEventArgs e)
    {
        // без проверки IsPressed: отпускание получают только тот, на кого
        // нажали, и тот, кто захватил мышь — маршрутизация в Form это уже
        // гарантирует. А у захватившего нажатия могло и не быть: DragList
        // берёт захват из OnPreviewMouseDown, когда попадание досталось
        // строке, и раньше просто не узнавал об отпускании
        if (e.Button == MouseButton.Left)
            IsPressed = false;

        OnMouseUp(e);
        MouseUp?.Invoke(this, e);
    }

    internal void RaiseClick(MouseClickEventArgs args)
    {
        switch (args.Button)
        {
            case MouseButton.Right:
                OnRightClick(args);
                RightClick?.Invoke(this, args);
                return;

            case MouseButton.Middle:
                OnMiddleClick(args);
                MiddleClick?.Invoke(this, args);
                return;
        }

        // двойной клик приходит вторым: сначала обычный Click с Count=1,
        // потом ещё один с Count=2 — так же ведут себя системные контролы
        if (args.Count >= 2)
        {
            OnDoubleClick(args);
            DoubleClick?.Invoke(this, args);

            if (args.Handled) return;
        }

        OnClick(args);
        Click?.Invoke(this, args);
    }

    internal void RaiseKeyDown(KeyEventArgs e)
    {
        OnKeyDown(e);
        KeyDown?.Invoke(this, e);
    }

    internal void RaiseKeyUp(KeyEventArgs e)
    {
        OnKeyUp(e);
        KeyUp?.Invoke(this, e);
    }

    internal void RaiseMouseWheel(MouseWheelEventArgs e)
    {
        OnMouseWheel(e);
        MouseWheel?.Invoke(this, e);
    }

    internal void RaisePointerDown(PointerEventArgs e)
    {
        OnPointerDown(e);
        PointerDown?.Invoke(this, e);
    }

    internal void RaisePointerMove(PointerEventArgs e)
    {
        OnPointerMove(e);
        PointerMove?.Invoke(this, e);
    }

    internal void RaisePointerUp(PointerEventArgs e)
    {
        OnPointerUp(e);
        PointerUp?.Invoke(this, e);
    }

    internal void RaisePointerCanceled(PointerCancelEventArgs e)
    {
        // состояние сбрасываем здесь, а не оставляем на обработчик:
        // MouseUp после отмены не придёт никогда, и элемент, забывший
        // сбросить IsPressed сам, останется нажатым до конца жизни
        bool wasPressed = IsPressed;
        IsPressed = false;

        OnPointerCanceled(e);
        PointerCanceled?.Invoke(this, e);

        if (wasPressed) InvalidateVisual();
    }

    // ===================

    public UIElement? Parent
    {
        get;
        internal set
        {
            if (ReferenceEquals(field, value)) return;

            field = value;
            BumpOwnerGeneration();
        }
    }

    internal Form? Owner
    {
        get;
        set
        {
            if (ReferenceEquals(field, value)) return;

            field = value;
            BumpOwnerGeneration();
        }
    }

    // Общий счётчик поколений: любое изменение Parent или Owner где угодно
    // разом обесценивает все кэши. Грубо, но структура дерева меняется
    // на порядки реже, чем FindOwner вызывается, а промахнуться мимо
    // инвалидации так невозможно — оба свойства объявлены здесь же.
    private static int _ownerGeneration;

    private static void BumpOwnerGeneration() => _ownerGeneration++;

    private Form? _cachedOwner;
    private int _cachedOwnerGeneration = -1;

    // ===== источник значений стилизуемых свойств =====

    // Значения лежат в обычных полях, здесь только источник: два бита
    // на свойство — «задавали вообще» и «задавали из кода пользователя».
    // Массивы создаются при первой записи: у большинства элементов явно
    // задано хорошо если одно свойство из пятидесяти
    private ulong[]? _assigned;
    private ulong[]? _local;

    /// <summary>Идёт применение темы: сеттеры помечают записи как «от темы»,
    /// чтобы явно заданные пользователем значения тема не перетирала.</summary>
    /// <remarks>ThreadStatic, по той же причине, что пул кистей в SkiaGraphics:
    /// снимковые тесты xunit идут параллельно, и общий флаг заставил бы
    /// сеттеры в одном потоке считать значения темой, пока тема
    /// применяется в другом.</remarks>
    [ThreadStatic]
    internal static bool ApplyingTheme;

    private static bool GetBit(ulong[]? bits, int index) =>
        bits is not null && (index >> 6) < bits.Length && (bits[index >> 6] & (1UL << index)) != 0;

    private static void SetBit(ref ulong[]? bits, int index)
    {
        int word = index >> 6;

        if (bits is null) bits = new ulong[word + 1];
        else if (word >= bits.Length) Array.Resize(ref bits, word + 1);

        bits[word] |= 1UL << index;
    }

    private static void ClearBit(ulong[]? bits, int index)
    {
        if (bits is null || (index >> 6) >= bits.Length) return;

        bits[index >> 6] &= ~(1UL << index);
    }

    /// <summary>Значение задавали: не умолчание и не унаследованное.</summary>
    public bool HasValue(StyledProperty property) => GetBit(_assigned, property.Index);

    /// <summary>Значение задали из кода — тема его больше не тронет.</summary>
    public bool IsLocal(StyledProperty property) => GetBit(_local, property.Index);

    /// <summary>Записать умолчание самого контрола. Тема такое значение
    /// перекроет, код пользователя — тем более. Только для конструкторов:
    /// обычное присваивание из них помечало бы свойство заданным вручную
    /// и навсегда закрывало от темы.</summary>
    protected void SetControlDefault<T>(StyledProperty<T> property, T value)
    {
        property.Write(this, value);

        SetBit(ref _assigned, property.Index);
        ClearBit(_local, property.Index);
    }

    /// <summary>Мост для редакторов свойств: записать значение так же,
    /// как это сделало бы обычное присваивание.</summary>
    internal bool SetStyledValue<T>(StyledProperty<T> property, T value) =>
        SetValue(property, value);

    /// <summary>Записать значение с учётом источника.
    /// false — запись отклонена: пишет тема, а свойство задали вручную.</summary>
    /// <summary>Записать значение с учётом источника.
    /// false — запись отклонена: пишет тема, а свойство задали вручную
    /// или оно под управлением биндинга.</summary>
    protected bool SetValue<T>(StyledProperty<T> property, ref T storage, T value)
    {
        // Лестница источников, сверху вниз: явное присваивание, биндинг,
        // тема, умолчание контрола. Тема не перебивает ни первое, ни второй.
        if (ApplyingTheme && (IsLocal(property) || IsBound(property))) return false;

        bool assigned = HasValue(property);

        if (assigned && EqualityComparer<T>.Default.Equals(storage, value))
        {
            // значение то же, но источник мог поменяться: пользователь
            // присвоил ровно то, что уже стояло от темы — и теперь это его
            if (!ApplyingTheme) SetBit(ref _local, property.Index);

            return false;
        }

        // явное присваивание из кода — вершина лестницы. В OneWay оно
        // разрывает связь: иначе следующее изменение источника молча
        // затрёт то, что написал пользователь, и он не поймёт почему.
        // В TwoWay значение уходит в источник, и связь сохраняется.
        if (!ApplyingTheme && IsBound(property))
            PushOrBreakBinding(property, value);

        // переход стартует от прежнего видимого значения — до записи
        BeginTransition(property, storage);
        storage = value;

        SetBit(ref _assigned, property.Index);

        if (ApplyingTheme) ClearBit(_local, property.Index);
        else SetBit(ref _local, property.Index);

        if (property.AffectsLayout) Invalidate();
        else InvalidateVisual();

        OnStyledPropertyChanged(property);
        return true;
    }

    /// <summary>Для свойств без собственного поля: значение живёт в другом
    /// объекте, а сюда мы только маршрутизируем. Запись идёт через делегат
    /// свойства, поэтому ref-хранилище не нужно.</summary>
    protected bool SetValue<T>(StyledProperty<T> property, T value)
    {
        if (ApplyingTheme && (IsLocal(property) || IsBound(property))) return false;

        bool assigned = HasValue(property);

        if (assigned && EqualityComparer<T>.Default.Equals(property.GetValue(this), value))
        {
            if (!ApplyingTheme) SetBit(ref _local, property.Index);
            return false;
        }

        if (!ApplyingTheme && IsBound(property))
            PushOrBreakBinding(property, value);

        BeginTransition(property, property.GetValue(this));
        property.Write(this, value);

        SetBit(ref _assigned, property.Index);

        if (ApplyingTheme) ClearBit(_local, property.Index);
        else SetBit(ref _local, property.Index);

        if (property.AffectsLayout) Invalidate();
        else InvalidateVisual();

        OnStyledPropertyChanged(property);
        return true;
    }

    /// <summary>Явное присваивание в привязанное свойство: TwoWay отдаёт
    /// значение источнику, OneWay разрывается.</summary>
    private void PushOrBreakBinding(StyledProperty property, object? value)
    {
        if (_bindings is null || !_bindings.TryGetValue(property.Index, out Binding? binding))
            return;

        if (binding.Mode == BindingMode.TwoWay)
            binding.PushToSource(value);
        else
            Unbind(property);
    }

    /// <summary>Значение стилизуемого свойства изменилось. Точка для реакций,
    /// которые не выражаются самой записью: остановить анимацию при скрытии,
    /// согласовать зависимые значения, пересчитать кэш.</summary>
    protected virtual void OnStyledPropertyChanged(StyledProperty property) { }

    /// <summary>Забыть заданное вручную и вернуть управление теме.</summary>
    public void ClearValue<T>(StyledProperty<T> property)
    {
        // привязка — тоже источник значения, и очистка снимает его вместе
        // с остальными: иначе источник продолжал бы писать в свойство,
        // которое считается очищенным
        Unbind(property);

        ClearBit(_local, property.Index);
        ClearBit(_assigned, property.Index);

        property.Write(this, property.DefaultValue);

        // тема могла бы задать своё — спрашиваем заново
        App.Theme.Apply(this);

        if (property.AffectsLayout) Invalidate();
        else InvalidateVisual();
    }

    /// <summary>Значение с учётом наследования.
    /// Заданное вручную — у себя или у любого предка — важнее того, что
    /// тема поставила этому элементу. Иначе panel.TextColor не дошёл бы
    /// до вложенных подписей: тема задаёт им цвет каждой лично.</summary>
    public T GetInheritedValue<T>(StyledProperty<T> property)
    {
        for (UIElement? current = this; current is not null; current = current.Parent)
            if (current.IsLocal(property))
                return property.GetValue(current);

        if (HasValue(property))
            return property.GetValue(this);

        return property.DefaultValue;
    }

    /// <summary>Сообщить биндингу, что значение свойства изменилось помимо
    /// присваивания. Нужно контролам, которые правят своё состояние
    /// напрямую: ввод в TextBox меняет документ, а не свойство,
    /// и лестница источников такое изменение не видит.</summary>
    protected void NotifyBoundValueChanged(StyledProperty property, object? value)
    {
        if (_bindings is null ||
            !_bindings.TryGetValue(property.Index, out Binding? binding) ||
            binding.Mode != BindingMode.TwoWay)
            return;

        binding.PushToSource(value);
    }

    public Point Position { get; set; }
    // Auto по умолчанию — авторазмер по контенту, пока явно не задан Size
    private Size _explicitSize = Size.Auto;
    private Size _actualSize = Size.Empty;

    /// <summary>Явно заданный размер. Size.Auto означает «подобрать по содержимому».</summary>
    public Size Size
    {
        get => _explicitSize;
        set
        {
            if (_explicitSize == value) return;

            _explicitSize = value;
            _actualSize = value;   // до первого layout рисуем по заданному
            Invalidate();
        }
    }

    /// <summary>Фактический размер после раскладки. Именно им рисуемся.</summary>
    public Size ActualSize => _actualSize;
    public Rectangle Rectangle => new(Position, _actualSize);
    public Rectangle LocalBounds => new(Point.Empty, SanitizedSize);
    public Rectangle ContentBounds => new(
        new Point(Padding.Left, Padding.Top),
        new Size(
            NonNegative(SanitizedSize.Width - Padding.Horizontal),
            NonNegative(SanitizedSize.Height - Padding.Vertical)));
    /// <summary>По какому прямоугольнику обрезать потомков. Обычно совпадает
    /// с ContentBounds; панели сужают его на место под полосы прокрутки.</summary>
    protected internal virtual Rectangle ClipBounds => ContentBounds;
    private Size SanitizedSize => new(
        float.IsFinite(_actualSize.Width) ? _actualSize.Width : 0f,
        float.IsFinite(_actualSize.Height) ? _actualSize.Height : 0f);
    private static float NonNegative(float value) =>
        float.IsFinite(value) && value > 0f ? value : 0f;

    [Styled(Category = "Appearance")]
    public partial CornerRadius CornerRadius { get; set; }
    private static CornerRadius CornerRadiusDefault => CornerRadius.Zero;

    protected internal bool HasTransform =>
        Rotation != 0f || ScaleX != 1f || ScaleY != 1f || TranslateX != 0f || TranslateY != 0f;

    /// <summary>Есть ли преобразование сложнее сдвига. Сдвиг обход дерева
    /// умеет складывать со смещением и потому не мешает отсечению,
    /// а поворот и масштаб — мешают.</summary>
    protected internal bool HasComplexTransform => Rotation != 0f || ScaleX != 1f || ScaleY != 1f;

    internal Point Center => new(ActualSize.Width / 2f, ActualSize.Height / 2f);

    /// <summary>Наложить преобразования элемента на холст. Вызывается после
    /// сдвига на Position, то есть уже в системе координат элемента.</summary>
    /// <remarks>
    /// Порядок один и тот же здесь и в обратном пересчёте точки: сдвиг,
    /// затем поворот и масштаб вокруг центра. Две реализации одного
    /// преобразования обязаны стоять рядом — разъехавшись, они дадут
    /// картинку в одном месте и клики в другом.
    /// </remarks>
    internal void ApplyTransform(Graphics g)
    {
        if (TranslateX != 0f || TranslateY != 0f)
            g.Translate(TranslateX, TranslateY);

        if (!HasComplexTransform) return;

        Point center = Center;

        g.Translate(center.X, center.Y);

        if (Rotation != 0f) g.Rotate(Rotation);
        if (ScaleX != 1f || ScaleY != 1f) g.Scale(ScaleX, ScaleY);

        g.Translate(-center.X, -center.Y);
    }

    /// <summary>Перевести точку из системы координат родителя в свою —
    /// обратное к ApplyTransform вместе со сдвигом на Position.</summary>
    internal Point TransformPointToLocal(Point pointInParentSpace)
    {
        var local = new Point(
            pointInParentSpace.X - Position.X - TranslateX,
            pointInParentSpace.Y - Position.Y - TranslateY);

        if (!HasComplexTransform) return local;

        Point center = Center;

        if (ScaleX != 0f && ScaleY != 0f && (ScaleX != 1f || ScaleY != 1f))
            local = new Point(
                center.X + (local.X - center.X) / ScaleX,
                center.Y + (local.Y - center.Y) / ScaleY);

        if (Rotation != 0f)
            local = RotateAround(local, center, -Rotation);

        return local;
    }

    internal static Point RotateAround(Point point, Point center, float degrees)
    {
        float radians = degrees * MathF.PI / 180f;
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);

        float dx = point.X - center.X;
        float dy = point.Y - center.Y;

        return new Point(
            center.X + dx * cos - dy * sin,
            center.Y + dx * sin + dy * cos);
    }

    public string? ToolTip { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Цвет подложки под текущее состояние. Переопределяйте здесь,
    /// а не рисуйте фон вручную: рамка и скругление подхватятся сами.</summary>
    protected virtual Color CurrentBackground => Background;

    /// <summary>Цвет рамки под текущее состояние.</summary>
    protected virtual Color CurrentBorderColor => BorderColor;

    public List<MenuItem>? ContextMenu { get; set; }
    // IGridPlaceable

    public Size DesiredSize { get; private set; }

    /// <summary>Принимает ли элемент текстовый ввод.</summary>
    /// <remarks>
    /// Признак живёт здесь, а не в каждом контроле, потому что решение
    /// о клавиатуре принимает форма по факту фокуса. Если бы контрол
    /// звал показ сам, каждый будущий ввод обязан был бы не забыть
    /// про мобильные платформы — и однажды забудет.
    /// </remarks>
    public virtual bool AcceptsTextInput => false;

    public virtual SoftKeyboardKind SoftKeyboardKind => SoftKeyboardKind.Text;

    public bool IsHitTestVisible { get; set; } = true;
    /// <summary>Принимать ли перетаскивание из системы. Приёмник ищется
    /// от попавшего элемента вверх, так что достаточно включить его
    /// на панели, а не на каждом потомке.</summary>
    public bool AllowDrop { get; set; }

    /// <summary>Курсор над элементом. Default — наследуется от предков.</summary>
    public CursorKind Cursor { get; set; } = CursorKind.Default;

    internal CursorKind EffectiveCursor
    {
        get
        {
            for (UIElement? current = this; current is not null; current = current.Parent)
                if (current.Cursor != CursorKind.Default)
                    return current.Cursor;

            return CursorKind.Arrow;
        }
    }

    /// <summary>Свой шрифт, а если не задан — ближайший заданный у предков,
    /// затем шрифт формы, иначе Font.Default.</summary>
    public Font EffectiveFont =>
        GetInheritedValue(FontProperty) ?? FindOwner()?.Font ?? Font.Default;

    protected bool IsHovered { get; set; }
    protected bool IsPressed { get; set; }

    /// <summary>Направление своё, а если не задано — унаследованное от предков,
    /// затем от формы.</summary>
    public FlowDirection EffectiveFlowDirection =>
        GetInheritedValue(FlowDirectionProperty)
        ?? FindOwner()?.FlowDirection
        ?? Core.Text.FlowDirection.LeftToRight;

    public bool IsRightToLeft => EffectiveFlowDirection == Core.Text.FlowDirection.RightToLeft;

    public Image RenderToImage()
    {
        // раскладка отложена до кадра, а снимок — это кадр вне очереди
        FindOwner()?.UpdateLayout();

        int width = (int)MathF.Ceiling(ActualSize.Width);
        int height = (int)MathF.Ceiling(ActualSize.Height);

        if (width <= 0 || height <= 0)
            throw new InvalidOperationException(
                "Элемент ещё не размещён (Size == 0). Снимок можно делать только после layout-прохода.");

        return ElementRenderer.Current.Render(this, width, height);
    }

    /// <summary>Непрозрачность элемента вместе с потомками: 1 — непрозрачен,
    /// 0 — не виден и не рисуется.</summary>
    /// <remarks>
    /// Свойство системы стилей, а не обычное: так оно получает переходы,
    /// появление и исчезание, и тему. Раскладку не трогает — прозрачный
    /// элемент по-прежнему занимает своё место. Значения за пределами
    /// [0; 1] рендерер приводит к ним сам: прибивать их в сеттере значило
    /// бы ломать интерполяцию, которая может на мгновение перелететь.
    /// </remarks>
    [Styled(Category = "Appearance")]
    public partial float Opacity { get; set; }
    private static float OpacityDefault => 1f;

    private EffectChain? _effects;

    /// <summary>Визуальные эффекты. Создаётся при первом обращении:
    /// у большинства элементов эффектов нет, и лишний объект им ни к чему.</summary>
    public EffectChain Effects
    {
        get
        {
            if (_effects is not null) return _effects;

            _effects = new EffectChain();
            _effects.Changed += (_, _) => InvalidateVisual();

            return _effects;
        }
    }

    internal EffectChain? EffectsOrNull => _effects;

    /// <summary>Прямоугольник, который надо перерисовать вместе с элементом:
    /// сам элемент плюс запас на рамку и тень, вылезающие за границы.</summary>
    public Rectangle DirtyBounds
    {
        get
        {
            Point absolute = GetAbsolutePosition();

            return LocalDirtyBounds.Offset(absolute.X, absolute.Y);
        }
    }

    /// <summary>То же, но от собственного левого верхнего угла. Обход дерева
    /// считает абсолютную позицию сам, накапливая её при спуске: подъём
    /// к корню на каждом элементе — это O(глубина) за элемент и O(n·глубина)
    /// за кадр.</summary>
    internal Rectangle LocalDirtyBounds
    {
        get
        {
            var bounds = new Rectangle(Point.Empty, ActualSize);

            if (_effects is { IsEmpty: false })
            {
                Thickness bleed = _effects.TotalBleed(LocalBounds);

                bounds = new Rectangle(
                    new Point(bounds.X - bleed.Left, bounds.Y - bleed.Top),
                    new Size(
                        bounds.Width + bleed.Horizontal,
                        bounds.Height + bleed.Vertical));
            }

            if (Rotation != 0f)
            {
                // описанный прямоугольник вокруг повёрнутого
                float radians = Math.Abs(Rotation) * MathF.PI / 180f;
                float cos = MathF.Abs(MathF.Cos(radians));
                float sin = MathF.Abs(MathF.Sin(radians));

                float w = ActualSize.Width * cos + ActualSize.Height * sin;
                float h = ActualSize.Width * sin + ActualSize.Height * cos;

                var center = new Point(
                    bounds.X + ActualSize.Width / 2f,
                    bounds.Y + ActualSize.Height / 2f);

                bounds = new Rectangle(
                    new Point(center.X - w / 2f, center.Y - h / 2f),
                    new Size(w, h));
            }

            if (BoxShadow is { } shadow)
            {
                float spread = shadow.Blur + shadow.Spread
                    + Math.Max(Math.Abs(shadow.OffsetX), Math.Abs(shadow.OffsetY));

                bounds = bounds.Inflate(spread);
            }

            return bounds.Inflate(2f);   // запас на сглаживание и рамку
        }
    }

    public abstract void Draw(Graphics g);

    // ===== Переходы =====

    private List<Transition>? _transitionRules;
    private List<IPropertyTransition>? _running;

    [ThreadStatic] private static int s_presentationDepth;

    /// <summary>Общий выключатель переходов: под системную настройку
    /// «уменьшить движение» и под тесты, которым анимация мешает.</summary>
    public static bool TransitionsEnabled { get; set; } = true;

    /// <summary>Правила переходов этого элемента: какое свойство и за какое
    /// время добирается до нового значения.</summary>
    /// <remarks>
    /// Присваивание при этом остаётся мгновенным. Свойство читается как
    /// цель — так его видят код, биндинги и PropertyGrid. Промежуточное
    /// значение отдаётся только внутри отрисовки, через Presented.
    /// </remarks>
    public IList<Transition> Transitions => _transitionRules ??= [];

    /// <summary>Идёт отрисовка: чтение свойств отдаёт промежуточные
    /// значения переходов вместо целей.</summary>
    internal static bool IsPresenting => s_presentationDepth > 0;

    /// <summary>Открыть область отрисовки. Обход дерева держит её на всё
    /// время обхода — вложенность нужна на случай снимка элемента,
    /// сделанного посреди чужой отрисовки.</summary>
    internal static PresentationScope BeginPresentation()
    {
        s_presentationDepth++;

        return default;
    }

    internal readonly struct PresentationScope : IDisposable
    {
        public void Dispose() => s_presentationDepth--;
    }

    /// <summary>Значение, которое должно уйти в отрисовку: промежуточное,
    /// пока идёт переход, и цель во всех остальных случаях. Вызывается
    /// из сгенерированных геттеров.</summary>
    protected T Presented<T>(StyledProperty<T> property, T target)
    {
        if (!IsPresenting || _running is null) return target;

        foreach (IPropertyTransition running in _running)
            if (ReferenceEquals(running.Property, property))
                return ((PropertyTransition<T>)running).Current;

        return target;
    }

    /// <summary>Запустить переход к новому значению. Зовётся до записи:
    /// стартовать надо от того, что видно сейчас.</summary>
    private void BeginTransition<T>(StyledProperty<T> property, T from)
    {
        if (!TransitionsEnabled) return;
        if (_transitionRules is null || _transitionRules.Count == 0) return;

        // элемент, которого ещё не показывали, не переходит, а появляется:
        // иначе каждая форма открывалась бы с проездом всех значений темы
        // от умолчаний к настоящим
        if (!_hasBeenArranged) return;

        if (FindOwner() is not { } owner) return;

        Transition? rule = null;

        foreach (Transition candidate in _transitionRules)
            if (ReferenceEquals(candidate.Property, property))
            {
                rule = candidate;
                break;
            }

        if (rule is null) return;

        if (property.AffectsLayout)
        {
            // промежуточное значение видит только отрисовка, а размеры
            // считаются по цели — переход такого свойства выглядел бы
            // как рассинхрон картинки и раскладки
            ZfContract.Fail(
                $"Переход на {property.Name} невозможен: свойство влияет " +
                "на раскладку. Анимируйте то, что рисуется, — цвет, " +
                "прозрачность, поворот.");

            return;
        }

        if (Interpolator.Find<T>() is not { } interpolate)
        {
            ZfContract.Fail(
                $"Переход на {property.Name} невозможен: для {typeof(T).Name} " +
                "нет интерполятора. Объявите его через Interpolator.Register.");

            return;
        }

        // без формы переход некому вести — часы кадра живут на ней
        if (FindOwner() is null) return;

        // прерванный переход продолжается с того места, где был,
        // а не прыгает к прежнему началу
        StartTransition(property, PresentedOrTarget(property, from), rule);
    }

    /// <summary>Значение, которое сейчас видно: промежуточное, если переход
    /// уже идёт, и переданное иначе.</summary>
    private T PresentedOrTarget<T>(StyledProperty<T> property, T target)
    {
        if (_running is null) return target;

        foreach (IPropertyTransition running in _running)
            if (ReferenceEquals(running.Property, property))
                return ((PropertyTransition<T>)running).Current;

        return target;
    }

    /// <summary>Завести переход свойства от заданного значения к его текущей
    /// цели. Цель берётся из самого свойства и читается каждый кадр.</summary>
    internal void StartTransition<T>(StyledProperty<T> property, T from, Transition rule)
    {
        if (FindOwner() is not { } owner) return;

        if (Interpolator.Find<T>() is not { } interpolate)
        {
            ZfContract.Fail(
                $"Переход на {property.Name} невозможен: для {typeof(T).Name} " +
                "нет интерполятора. Объявите его через Interpolator.Register.");

            return;
        }

        var transition = new PropertyTransition<T>(this, property, from, rule, interpolate);

        _running ??= [];
        _running.Add(transition);

        // вытеснение прежнего перехода того же свойства — по ключу, в часах
        owner.AddAnimation(transition);
    }

    internal void RemoveTransition(IPropertyTransition transition)
    {
        if (_running is null) return;

        _running.Remove(transition);

        if (_running.Count == 0) _running = null;

        InvalidateVisual();
    }

    /// <summary>Кадр перехода: перерисовать элемент, не трогая раскладку.</summary>
    internal void InvalidateTransitionVisual() => InvalidateVisual();

    private Point _arrangedPosition;
    private bool _skipLayoutTransition;
    private bool _skipEnterTransition;
    private bool _skipExitTransition;

    /// <summary>Как элемент появляется на уже показанном экране.
    /// null — мгновенно, как и было.</summary>
    public VisibilityTransition? EnterTransition { get; set; }

    /// <summary>Как элемент исчезает, когда его убирают из панели.
    /// null — мгновенно, как и было.</summary>
    public VisibilityTransition? ExitTransition { get; set; }

    internal VisibilityTransition? EffectiveEnterTransition =>
        EnterTransition ?? (Parent as PanelControl)?.ChildrenEnterTransition;

    /// <summary>Правило исчезания относительно конкретной панели: спрашивают
    /// в момент удаления, когда Parent уже может быть сброшен.</summary>
    internal VisibilityTransition? ExitTransitionIn(PanelControl panel) =>
        ExitTransition ?? panel.ChildrenExitTransition;

    /// <summary>Не считать ближайшее появление и удаление появлением
    /// и исчезанием. Нужно тем, кто создаёт и выбрасывает контейнеры
    /// по ходу прокрутки: строка, выехавшая из-за края, не появилась —
    /// она просто стала видна.</summary>
    internal void SkipNextVisibilityTransitions()
    {
        _skipEnterTransition = true;
        _skipExitTransition = true;
    }

    /// <summary>Забрать отметку о пропуске исчезания: она одноразовая.</summary>
    internal bool ConsumeSkipExit()
    {
        bool skip = _skipExitTransition;
        _skipExitTransition = false;

        return skip;
    }

    /// <summary>Годится ли элемент в уходящие: исчезать может только то,
    /// что уже было на экране.</summary>
    internal bool CanAnimateExit => TransitionsEnabled && _hasBeenArranged && IsVisible;

    private void StartEnterTransition()
    {
        if (!TransitionsEnabled) return;
        if (EffectiveEnterTransition is not { } rule) return;
        if (!IsEffectivelyVisible) return;

        // из невидимого вида к тому, что лежит в свойствах: сами свойства
        // не трогаем, модель с первого кадра видит итоговые значения
        if (rule.Opacity != 1f)
            StartTransition(OpacityProperty, Opacity * rule.Opacity, rule.ForOpacity);

        if (rule.Scale != 1f)
        {
            StartTransition(ScaleXProperty, ScaleX * rule.Scale, rule.ForScaleX);
            StartTransition(ScaleYProperty, ScaleY * rule.Scale, rule.ForScaleY);
        }

        if (rule.OffsetX != 0f)
            StartTransition(TranslateXProperty, TranslateX + rule.OffsetX, rule.ForTranslateX);

        if (rule.OffsetY != 0f)
            StartTransition(TranslateYProperty, TranslateY + rule.OffsetY, rule.ForTranslateY);
    }

    /// <summary>Не считать ближайший переезд переездом. Нужно тем, кто
    /// переиспользует контейнеры: строка, перепривязанная к другому
    /// элементу списка, не переехала — она стала другой строкой,
    /// и вести её через пол-экрана незачем.</summary>
    internal void SkipNextLayoutTransition() => _skipLayoutTransition = true;

    /// <summary>Как элемент переезжает, когда раскладка ставит его
    /// на новое место. null — мгновенно, как и было.</summary>
    /// <remarks>
    /// Правило берётся у самого элемента, а если его нет — у панели-родителя:
    /// одной строкой на списке включается плавное перестроение всех его
    /// строк, и контролам внутри ничего знать об этом не нужно.
    /// </remarks>
    public LayoutTransition? LayoutTransition { get; set; }

    internal LayoutTransition? EffectiveLayoutTransition =>
        LayoutTransition ?? (Parent as PanelControl)?.ChildrenLayoutTransition;

    /// <summary>Переезд по правилу FLIP: элемент уже стоит на новом месте,
    /// но рисуется от старого и приезжает к нулевому сдвигу.</summary>
    private void StartLayoutTransition(Point previous, Point placed)
    {
        if (!TransitionsEnabled) return;
        if (EffectiveLayoutTransition is not { } rule) return;

        // невидимое поддерево не переезжает: страница, перестроенная
        // за кадром, должна открыться сразу на своих местах
        if (!IsEffectivelyVisible) return;

        float dx = previous.X - placed.X;
        float dy = previous.Y - placed.Y;

        // незакончившийся прошлый переезд складывается с новым: иначе
        // строка, которую двигают дважды подряд, дёрнется на середине
        if (dx != 0f)
            StartTransition(
                TranslateXProperty,
                PresentedOrTarget(TranslateXProperty, TranslateX) + dx,
                rule.ForTranslateX);

        if (dy != 0f)
            StartTransition(
                TranslateYProperty,
                PresentedOrTarget(TranslateYProperty, TranslateY) + dy,
                rule.ForTranslateY);
    }

    // ===== Measure/Arrange =====

    private Size _measuredAgainst;
    private bool _measureValid;

    /// <summary>Кэш измерения. Выключается на случай подозрения, что
    /// где-то не хватает Invalidate: сравнить поведение с кэшем и без —
    /// самый быстрый способ это подтвердить.</summary>
    public static bool MeasureCacheEnabled { get; set; } = true;

    /// <summary>Проверять кэш вместо того, чтобы ему верить: измерение
    /// считается заново и сравнивается с запомненным, расхождение
    /// сообщается через ZfContract. Расхождение означает, что состояние
    /// элемента поменялось без Invalidate. Режим отладочный — измерений
    /// вдвое больше, чем без кэша вообще.</summary>
    public static bool VerifyMeasureCache { get; set; }

    /// <remarks>
    /// Повторное измерение с тем же ограничением возвращает тот же
    /// результат — если состояние элемента не менялось. Менялось ли,
    /// говорит Invalidate: он помечает элемент и всех предков, чей размер
    /// считается по нему. Без этого кэша полный проход измерял каждый
    /// элемент на каждом кадре, хотя за кадр меняются единицы.
    ///
    /// Цена — требование к контролам: свойство, влияющее на размер,
    /// обязано звать Invalidate. Для [Styled] с AffectsLayout это делает
    /// сама система свойств, для обычных — автор свойства.
    /// </remarks>
    public void Measure(Size availableSize)
    {
        bool reusable = MeasureCacheEnabled
            && _measureValid
            && SameConstraint(_measuredAgainst, availableSize);

        if (reusable && !VerifyMeasureCache) return;

        Size measured = MeasureOverride(availableSize);

        if (reusable && measured != DesiredSize)
        {
            ZfContract.Fail(
                $"{GetType().Name} \"{Name}\": измерение дало {measured}, " +
                $"а в кэше лежит {DesiredSize}. Значит состояние элемента " +
                "изменилось без Invalidate — свойство, влияющее на размер, " +
                "обязано его звать.");
        }

        DesiredSize = measured;
        _measuredAgainst = availableSize;
        _measureValid = true;
    }

    /// <summary>Пометить измерение устаревшим у себя и у предков: их размер
    /// считается по нашему.</summary>
    /// <remarks>
    /// Подъём обрывается на первом уже помеченном элементе: если элемент
    /// недействителен, то и все его предки — их помечали вместе с ним.
    /// </remarks>
    internal void InvalidateMeasure()
    {
        for (UIElement? current = this; current is not null; current = current.Parent)
        {
            if (!current._measureValid) break;

            current._measureValid = false;
        }
    }

    /// <summary>Бесконечности сравниваются как равные, NaN — тоже:
    /// «ограничений нет» и «ось авто» должны совпадать сами с собой,
    /// иначе кэш не сработает никогда.</summary>
    private static bool SameConstraint(Size a, Size b) =>
        (a.Width == b.Width || (float.IsNaN(a.Width) && float.IsNaN(b.Width)))
        && (a.Height == b.Height || (float.IsNaN(a.Height) && float.IsNaN(b.Height)));

    private bool _hasBeenArranged;

    public void Arrange(Rectangle finalRect)
    {
        // Dock.Fill — явное требование занять всё, оно перекрывает выравнивание
        bool fill = Docking == Dock.Fill;

        bool stretchH = fill || HorizontalAlignment == HorizontalAlignment.Stretch;
        bool stretchV = fill || VerticalAlignment == VerticalAlignment.Stretch;

        float width = stretchH ? finalRect.Width : Math.Min(DesiredSize.Width, finalRect.Width);
        float height = stretchV ? finalRect.Height : Math.Min(DesiredSize.Height, finalRect.Height);

        float x = stretchH ? finalRect.X : HorizontalAlignment switch
        {
            HorizontalAlignment.Right => finalRect.X + finalRect.Width - width,
            HorizontalAlignment.Center => finalRect.X + (finalRect.Width - width) / 2f,
            _ => finalRect.X,
        };

        float y = stretchV ? finalRect.Y : VerticalAlignment switch
        {
            VerticalAlignment.Bottom => finalRect.Y + finalRect.Height - height,
            VerticalAlignment.Center => finalRect.Y + (finalRect.Height - height) / 2f,
            _ => finalRect.Y,
        };

        var placed = new Point(x, y);

        // первое размещение внутри уже показанного родителя — это
        // появление: элемент добавили на экран, который уже видели.
        // Первое размещение вместе с родителем — это просто открытие
        // формы, анимировать его незачем
        bool appearing = !_hasBeenArranged && Parent is { _hasBeenArranged: true };

        // сравниваем с местом из прошлой раскладки, а не с Position:
        // прокручивающая панель правит Position детей уже после Arrange,
        // и переезд на величину прокрутки анимировать не надо
        Point previous = _arrangedPosition;
        bool moved = _hasBeenArranged && (previous.X != placed.X || previous.Y != placed.Y);

        Position = placed;
        _arrangedPosition = placed;

        // результат раскладки уходит в ActualSize; Size остаётся тем,
        // что задал пользователь, иначе авторазмер сработает лишь однажды
        _actualSize = ArrangeOverride(new Size(width, height));

        if (moved && !_skipLayoutTransition) StartLayoutTransition(previous, placed);

        _skipLayoutTransition = false;

        if (appearing && !_skipEnterTransition) StartEnterTransition();

        _skipEnterTransition = false;

        // первый проход только фиксирует размер: наследники, реагирующие
        // на изменение, не должны срабатывать на переходе из «не размещён»
        if (_hasBeenArranged)
            OnSizeChanged();
        else
            _hasBeenArranged = true;
    }

    public void Arrange(Point point, Size size) => Arrange(new Rectangle(point, size));

    public Point GetAbsolutePosition()
    {
        float x = 0, y = 0;

        for (UIElement? current = this; current is not null; current = current.Parent)
        {
            x += current.Position.X;
            y += current.Position.Y;
        }

        return new Point(x, y);
    }

    // Дефолт для листовых контролов, которые не переопределили MeasureOverride:
    // если Size задан явно — используем его, иначе (Auto) считаем, что "хочу 0".
    protected virtual Size MeasureOverride(Size availableSize) =>
        ResolveSize(Size.Empty, availableSize);

    // Дефолт — просто заполнить всё, что дал родитель ("stretch").
    protected virtual Size ArrangeOverride(Size finalSize) => finalSize;

    // Общий помощник: явно заданная ось Size побеждает contentSize,
    // авто-ось (NaN) берёт вычисленный по контенту размер, и то и другое
    // не может превышать то, что реально выделил родитель.
    protected Size ResolveSize(Size contentSize, Size availableSize)
    {
        float w = _explicitSize.IsWidthAuto ? contentSize.Width : _explicitSize.Width;
        float h = _explicitSize.IsHeightAuto ? contentSize.Height : _explicitSize.Height;

        w = Math.Min(w, availableSize.Width);
        h = Math.Min(h, availableSize.Height);

        // бесконечность не должна доезжать до размеров: она означает
        // «ограничений нет», а не «элемент бесконечный»
        return new Size(
            float.IsFinite(w) ? w : 0f,
            float.IsFinite(h) ? h : 0f);
    }

    // ===== события мыши/фокуса (без изменений) =====

    /// <summary>Перерисовать только этот элемент, без пересчёта раскладки.</summary>
    protected internal void InvalidateVisual()
    {
        if (!float.IsFinite(ActualSize.Width) || !float.IsFinite(ActualSize.Height))
            return;

        FindOwner()?.InvalidateRect(DirtyBounds);
    }

    // protected internal — доступен и наследникам (как раньше), и коду
    // внутри сборки вроде FocusDispatcher, которому нужно попросить
    // перерисовку не будучи подклассом UIElement.
    /// <summary>Изменилась геометрия — нужен полный пересчёт и перерисовка.</summary>
    protected internal void Invalidate()
    {
        // сначала свой кэш измерения и кэш предков, потом просьба к форме:
        // без этого форма пересчитала бы раскладку по старым размерам
        InvalidateMeasure();

        FindOwner()?.Invalidate();
    }

    /// <summary>Захват от имени распознавателя. Отдельный вход потому,
    /// что CaptureMouse защищённый: снаружи элемента его не позвать,
    /// а распознаватель живёт снаружи.</summary>
    internal void CaptureForGesture() => FindOwner()?.CaptureMouse(this);

    protected void CaptureMouse() => FindOwner()?.CaptureMouse(this);

    protected void ReleaseMouseCapture() => FindOwner()?.ReleaseMouseCapture(this);

    protected virtual void OnAttached()
    {
        // called when element (first time?) added to form (and\or parent?)
    }

    protected virtual void OnSizeChanged()
    {
        // called when size changed. TODO: Dont call this before size assigned first time
    }

    internal Form? FindOwner()
    {
        if (_cachedOwnerGeneration == _ownerGeneration)
            return _cachedOwner;

        UIElement root = this;
        while (root.Parent is not null)
            root = root.Parent;

        _cachedOwner = root.Owner;
        _cachedOwnerGeneration = _ownerGeneration;

        return _cachedOwner;
    }
    protected virtual void OnGotFocus() { }
    protected virtual void OnLostFocus() { }

    internal void RaisePreviewMouseDown(MouseButtonEventArgs e) => OnPreviewMouseDown(e);

    internal void RaisePreviewKeyDown(KeyEventArgs e)
    {
        OnPreviewKeyDown(e);
        PreviewKeyDown?.Invoke(this, e);
    }

    /// <summary>Реагирует ли контрол на пробел/Enter как на клик (кнопки, чекбоксы).</summary>
    protected virtual bool IsKeyActivatable => false;

    protected virtual void OnKeyDown(KeyEventArgs e)
    {
        if (!IsKeyActivatable) return;

        if (e.Key is Key.Space or Key.Enter)
        {
            // клик "из центра себя" — координата нужна тем, кто её читает
            // (например, ListBox определяет по ней строку)
            Point absolute = GetAbsolutePosition();
            var center = new Point(
                absolute.X + ActualSize.Width / 2f,
                absolute.Y + ActualSize.Height / 2f);

            RaiseClick(new MouseClickEventArgs(MouseButton.Left, MouseButtonState.Up, center, 1));
            e.Handled = true;
        }
    }

    protected virtual void OnDetached() { }

    internal void RaiseDetached()
    {
        OnDetached();
        Detached?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Рисуется после потомков и вне их отсечения — полосы прокрутки, рамки поверх.</summary>
    protected internal virtual void DrawOverlay(Graphics g) { }

    /// <summary>Забрать попадание себе, не спускаясь к потомкам (зона полосы прокрутки).</summary>
    protected internal virtual bool HitTestSelfFirst(Point localPoint) => false;
}

public static class UIElementEx 
{
    /// <summary>Настроить элемент по месту, не разрывая выражение.</summary>
    public static T With<T>(this T element, Action<T> configure) where T : UIElement
    {
        configure(element);
        return element;
    }

    /// <summary>Позиция в Grid, не разрывая выражение.</summary>
    public static T At<T>(this T element, int row, int column) where T : UIElement
    {
        element.Row = row;
        element.Column = column;
        return element;
    }
}
