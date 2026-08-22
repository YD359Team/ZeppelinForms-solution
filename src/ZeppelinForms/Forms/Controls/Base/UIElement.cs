using System.Diagnostics;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>
/// Base element of any UI tree node
/// </summary>
public abstract class UIElement : IGridPlaceable
{
    public event EventHandler<MouseClickEventArgs>? Click;

    public UIElement? Parent { get; internal set; }
    public Dock Docking { get; set; }
    public Point Position { get; set; }
    public Size Size
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnArrange();
        }
    }
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
    public string Name { get; set; } = string.Empty;
    public Color Background { get; set; } = Colors.Transparent;
    // IGridPlaceable
    public int Row { get; set; }
    public int Column { get; set; }

    protected bool IsHovered { get; set; }
    protected bool IsPressed { get; set; }

    internal Form? Owner { get; set; }

    public Image RenderToImage()
    {
        throw new NotImplementedException();
        // TODO: add rendering to image
    }

    public abstract void Draw(Graphics g);

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

    protected virtual void OnArrange()
    {
        // called when parent panel moved\resized this element
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
}
