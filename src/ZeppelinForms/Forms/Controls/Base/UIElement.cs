using System.Diagnostics;
using ZeppelinForms.Animation;
using ZeppelinForms.Core.Text;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Effects;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>
/// Base element of any UI tree node
/// </summary>
public abstract partial class UIElement : IGridPlaceable, IBorderedElement
{
    // ===== события =====

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

    // ===
    [Styled(Category = "Appearance")]
    public partial Color Background { get; set; }
    private static Color BackgroundDefault => Colors.Transparent;

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

    [Styled(Category = "Text", AffectsLayout = true)]
    public partial Font? Font { get; set; }
    //
    /// <summary>Поворот в градусах вокруг центра элемента.</summary>
    [Styled(Category = "Appearance")]
    public partial float Rotation { get; set; }

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
    protected virtual void OnPreviewMouseDown(Point location) { }
    protected virtual void OnPreviewKeyDown(KeyEventArgs e) { }
    protected virtual void OnKeyUp(KeyEventArgs e) { }
    protected virtual void OnTextInput(char c) { }

    internal void RaiseTextInput(char c) => OnTextInput(c);

    // ===== подъём событий =====

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
        if (e.Button == MouseButton.Left)
        {
            if (!IsPressed) return;
            IsPressed = false;
        }

        OnMouseUp(e);
        MouseUp?.Invoke(this, e);

        InvalidateVisual();
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

    /// <summary>Идёт применение темы: записи помечаются как «от темы»,
    /// а не «задано вручную». Ставится Theme.Apply на время обхода.
    /// Дерево и тема живут в UI-потоке, поэтому статики достаточно.</summary>
    internal static bool ApplyingTheme { get; set; }

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

    /// <summary>Записать значение с учётом источника.
    /// false — запись отклонена: пишет тема, а свойство задали вручную.</summary>
    protected bool SetValue<T>(StyledProperty<T> property, ref T storage, T value)
    {
        if (ApplyingTheme && IsLocal(property)) return false;

        bool assigned = HasValue(property);

        if (assigned && EqualityComparer<T>.Default.Equals(storage, value))
        {
            // значение то же, но источник мог поменяться: пользователь
            // присвоил ровно то, что уже стояло от темы — и теперь это его
            if (!ApplyingTheme) SetBit(ref _local, property.Index);

            return false;
        }

        storage = value;

        SetBit(ref _assigned, property.Index);

        if (ApplyingTheme) ClearBit(_local, property.Index);
        else SetBit(ref _local, property.Index);

        if (property.AffectsLayout) Invalidate();
        else InvalidateVisual();

        return true;
    }

    /// <summary>Для свойств без собственного поля: значение живёт в другом
    /// объекте, а сюда мы только маршрутизируем. Запись идёт через делегат
    /// свойства, поэтому ref-хранилище не нужно.</summary>
    protected bool SetValue<T>(StyledProperty<T> property, T value)
    {
        if (ApplyingTheme && IsLocal(property)) return false;

        bool assigned = HasValue(property);

        if (assigned && EqualityComparer<T>.Default.Equals(property.GetValue(this), value))
        {
            if (!ApplyingTheme) SetBit(ref _local, property.Index);
            return false;
        }

        property.Write(this, value);

        SetBit(ref _assigned, property.Index);

        if (ApplyingTheme) ClearBit(_local, property.Index);
        else SetBit(ref _local, property.Index);

        if (property.AffectsLayout) Invalidate();
        else InvalidateVisual();

        return true;
    }

    /// <summary>Забыть заданное вручную и вернуть управление теме.</summary>
    public void ClearValue<T>(StyledProperty<T> property)
    {
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

    protected internal bool HasTransform => Rotation != 0f;
    internal Point Center => new(ActualSize.Width / 2f, ActualSize.Height / 2f);
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
    public bool IsHitTestVisible { get; set; } = true;

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
        int width = (int)MathF.Ceiling(ActualSize.Width);
        int height = (int)MathF.Ceiling(ActualSize.Height);

        if (width <= 0 || height <= 0)
            throw new InvalidOperationException(
                "Элемент ещё не размещён (Size == 0). Снимок можно делать только после layout-прохода.");

        return ElementRenderer.Current.Render(this, width, height);
    }

    private float _opacity = 1f;

    public float Opacity
    {
        get => _opacity;
        set
        {
            float clamped = Math.Clamp(value, 0f, 1f);
            if (Math.Abs(_opacity - clamped) < 0.001f) return;

            _opacity = clamped;
            Invalidate();
        }
    }

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
            var bounds = new Rectangle(GetAbsolutePosition(), ActualSize);

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

    // ===== Measure/Arrange =====

    public void Measure(Size availableSize)
    {
        DesiredSize = MeasureOverride(availableSize);
    }

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

        Position = new Point(x, y);

        // результат раскладки уходит в ActualSize; Size остаётся тем,
        // что задал пользователь, иначе авторазмер сработает лишь однажды
        _actualSize = ArrangeOverride(new Size(width, height));

        // первый проход только фиксирует размер: наследники, реагирующие
        // на изменение, не должны срабатывать на переходе из «не размещён»
        if (_hasBeenArranged)
            OnSizeChanged();
        else
            _hasBeenArranged = true;
    }

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
    protected internal void Invalidate() => FindOwner()?.Invalidate();

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

    internal void RaiseAttached() => OnAttached();

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

    internal void RaiseGotFocus() => OnGotFocus();
    internal void RaiseLostFocus() => OnLostFocus();

    internal void RaisePreviewMouseDown(Point location) => OnPreviewMouseDown(location);

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

    internal void RaiseDetached() => OnDetached();

    /// <summary>Рисуется после потомков и вне их отсечения — полосы прокрутки, рамки поверх.</summary>
    protected internal virtual void DrawOverlay(Graphics g) { }

    /// <summary>Забрать попадание себе, не спускаясь к потомкам (зона полосы прокрутки).</summary>
    protected internal virtual bool HitTestSelfFirst(Point localPoint) => false;
}
