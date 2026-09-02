using System.Diagnostics;
using ZeppelinForms.Animation;
using ZeppelinForms.Core.Text;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>
/// Base element of any UI tree node
/// </summary>
public abstract class UIElement : IGridPlaceable
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

    public UIElement? Parent { get; internal set; }
    /// <summary>
    /// Доля свободного места по главной оси панели. 0 — элемент занимает
    /// желаемый размер, больше нуля — делит остаток пропорционально весу.
    /// Учитывается только панелями с главной осью (StackPanel, DockPanel).
    /// </summary>
    public float FlexGrow { get; set; }
    public Dock Docking { get; set; }
    public HorizontalAlignment HorizontalAlignment { get; set; } = HorizontalAlignment.Stretch;
    public VerticalAlignment VerticalAlignment { get; set; } = VerticalAlignment.Stretch;
    public FlowDirection? FlowDirection { get; set; }
    public Point Position { get; set; }
    // Auto по умолчанию — авторазмер по контенту, пока явно не задан Size
    private Size _explicitSize = Size.Auto;
    private Size _actualSize = Size.Empty;

    /// <summary>Насколько приглушать выключенный элемент.</summary>
    public float DisabledOpacity { get; set; } = 0.5f;
    public float DisabledDesaturation { get; set; } = 0.6f;

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
    public Thickness Margin { get; set; } = Thickness.Zero;
    public Thickness Padding { get; set; } = Thickness.Zero;
    public Rectangle Rectangle => new(Position, _actualSize);
    public Rectangle LocalBounds => new(Point.Empty, SanitizedSize);
    public Rectangle ContentBounds => new(
        new Point(Padding.Left, Padding.Top),
        new Size(
            NonNegative(SanitizedSize.Width - Padding.Horizontal),
            NonNegative(SanitizedSize.Height - Padding.Vertical)));
    private Size SanitizedSize => new(
        float.IsFinite(_actualSize.Width) ? _actualSize.Width : 0f,
        float.IsFinite(_actualSize.Height) ? _actualSize.Height : 0f);
    private static float NonNegative(float value) =>
        float.IsFinite(value) && value > 0f ? value : 0f;
    public CornerRadius CornerRadius { get; set; } = CornerRadius.Zero;
    /// <summary>Поворот в градусах вокруг центра элемента.</summary>
    public float Rotation { get; set; }
    protected internal bool HasTransform => Rotation != 0f;
    internal Point Center => new(ActualSize.Width / 2f, ActualSize.Height / 2f);
    public bool IsEnabled { get; set; } = true;
    public bool IsVisible { get; set; } = true;
    public string? ToolTip { get; set; }
    public string Name { get; set; } = string.Empty;
    public Color Background { get; set; } = Colors.Transparent;
    public List<MenuItem>? ContextMenu { get; set; }
    // IGridPlaceable
    public int Row { get; set; }
    public int Column { get; set; }
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

    public Font? Font { get; set; }

    /// <summary>Свой шрифт, а если не задан — ближайший заданный у предков, иначе Font.Default.</summary>
    public Font EffectiveFont
    {
        get
        {
            for (UIElement? current = this; current is not null; current = current.Parent)
            {
                if (current.Font is not null)
                    return current.Font;

                if (current.Parent is null)
                    return current.Owner?.Font ?? Font.Default;
            }

            return Font.Default;
        }
    }

    protected bool IsHovered { get; set; }
    protected bool IsPressed { get; set; }

    internal Form? Owner { get; set; }

    /// <summary>Направление своё, а если не задано — унаследованное от предков.</summary>
    public FlowDirection EffectiveFlowDirection
    {
        get
        {
            for (UIElement? current = this; current is not null; current = current.Parent)
            {
                if (current.FlowDirection is FlowDirection direction)
                    return direction;

                if (current.Parent is null)
                    return current.Owner?.FlowDirection ?? Core.Text.FlowDirection.LeftToRight;
            }

            return Core.Text.FlowDirection.LeftToRight;
        }
    }

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

    /// <summary>Прямоугольник, который надо перерисовать вместе с элементом:
    /// сам элемент плюс запас на рамку и тень, вылезающие за границы.</summary>
    public Rectangle DirtyBounds
    {
        get
        {
            var bounds = new Rectangle(GetAbsolutePosition(), ActualSize);

            if (Rotation != 0f)
            {
                // описанный прямоугольник вокруг повёрнутого
                float radians = Math.Abs(Rotation) * MathF.PI / 180f;
                float cos = MathF.Abs(MathF.Cos(radians));
                float sin = MathF.Abs(MathF.Sin(radians));

                float w = ActualSize.Width * cos + ActualSize.Height * sin;
                float h = ActualSize.Width * sin + ActualSize.Height * cos;

                var center = new Point(
                    bounds.X + Size.Width / 2f,
                    bounds.Y + Size.Height / 2f);

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

    public BoxShadow? BoxShadow { get; set; }

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

    protected virtual void OnAttached()
    {
        // called when element (first time?) added to form (and\or parent?)
    }

    protected virtual void OnSizeChanged()
    {
        // called when size changed. TODO: Dont call this before size assigned first time
    }

    internal void RaiseAttached() => OnAttached();

    // TODO: кешировать owner
    internal Form? FindOwner()
    {
        UIElement root = this;
        while (root.Parent is not null)
            root = root.Parent;

        return root.Owner;
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
