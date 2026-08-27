using System.Diagnostics;
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
    public event EventHandler<MouseClickEventArgs>? Click;
    public event EventHandler<MouseWheelEventArgs>? MouseWheel;
    public event EventHandler<KeyEventArgs>? KeyDown;

    public UIElement? Parent { get; internal set; }
    public Dock Docking { get; set; }
    public Point Position { get; set; }
    // Auto по умолчанию — авторазмер по контенту, пока явно не задан Size
    public Size Size { get; set; } = Size.Auto;
    public Thickness Margin { get; set; } = Thickness.Zero;
    public Thickness Padding { get; set; } = Thickness.Zero;
    public Rectangle Rectangle => new(Position, Size);
    public Rectangle LocalBounds => new(Point.Empty, Size);
    public Rectangle ContentBounds => new(
        new Point(Padding.Left, Padding.Top),
        new Size(
            Math.Max(0, Size.Width - Padding.Horizontal),
            Math.Max(0, Size.Height - Padding.Vertical)));
    public bool IsEnabled { get; set; } = true;
    public bool IsVisible { get; set; } = true;
    public string? ToolTip { get; set; }
    public string Name { get; set; } = string.Empty;
    public Color Background { get; set; } = Colors.Transparent;
    // IGridPlaceable
    public int Row { get; set; }
    public int Column { get; set; }
    public Size DesiredSize { get; private set; }
    public bool IsHitTestVisible { get; set; } = true;

    protected bool IsHovered { get; set; }
    protected bool IsPressed { get; set; }

    internal Form? Owner { get; set; }

    public Image RenderToImage()
    {
        throw new NotImplementedException();
        // TODO: add rendering to image
    }

    public abstract void Draw(Graphics g);

    // ===== Measure/Arrange =====

    public void Measure(Size availableSize)
    {
        DesiredSize = MeasureOverride(availableSize);
    }

    public void Arrange(Rectangle finalRect)
    {
        Position = finalRect.AsPosition();
        Size = ArrangeOverride(finalRect.AsSize());
        OnSizeChanged();
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
        float w = Size.IsWidthAuto ? contentSize.Width : Size.Width;
        float h = Size.IsHeightAuto ? contentSize.Height : Size.Height;
        return new Size(Math.Min(w, availableSize.Width), Math.Min(h, availableSize.Height));
    }

    // ===== события мыши/фокуса (без изменений) =====

    internal void RaiseMouseOver()
    {
        if (IsHovered) return;
        IsHovered = true;
        OnMouseOver();
        Invalidate();
    }

    internal void RaiseMouseLeave()
    {
        if (!IsHovered) return;
        IsHovered = false;
        OnMouseLeave();
        Invalidate();
    }

    // protected internal — доступен и наследникам (как раньше), и коду
    // внутри сборки вроде FocusDispatcher, которому нужно попросить
    // перерисовку не будучи подклассом UIElement.
    protected internal void Invalidate()
    {
        Debug.WriteLine($"UIElement.Invalidate {this.GetType().Name}");
        UIElement root = this;
        while (root.Parent is not null)
            root = root.Parent;

        root.Owner?.Invalidate();
    }

    protected virtual void OnAttached()
    {
        // called when element (first time?) added to form (and\or parent?)
    }

    protected virtual void OnSizeChanged()
    {
        // called when size changed. TODO: Dont call this before size assigned first time
    }

    protected virtual void OnMouseOver() { }
    protected virtual void OnMouseLeave() { }
    protected virtual void OnMouseDown() { }
    protected virtual void OnMouseUp() { }
    protected virtual void OnClick(MouseClickEventArgs e) { }
    protected virtual void OnMouseWheel(MouseWheelEventArgs e) { }
    protected virtual void OnKeyDown(KeyEventArgs e) { }

    internal void RaiseMouseDown()
    {
        IsPressed = true;
        OnMouseDown();
        Invalidate();
    }

    internal void RaiseMouseUp()
    {
        if (!IsPressed) return;
        IsPressed = false;
        OnMouseUp();
        Invalidate();
    }

    internal void RaiseClick(MouseButton button, Point location)
    {
        var args = new MouseClickEventArgs(button, MouseButtonState.Up, location);
        OnClick(args);
        Click?.Invoke(this, args);
    }

    internal void RaiseMouseWheel(MouseWheelEventArgs e)
    {
        OnMouseWheel(e);
        MouseWheel?.Invoke(this, e);
    }

    internal void RaiseKeyDown(KeyEventArgs e)
    {
        OnKeyDown(e);
        KeyDown?.Invoke(this, e);
    }

    internal void RaiseAttached() => OnAttached();

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

    protected virtual void OnTextInput(char c) { }
    internal void RaiseTextInput(char c) => OnTextInput(c);
}
