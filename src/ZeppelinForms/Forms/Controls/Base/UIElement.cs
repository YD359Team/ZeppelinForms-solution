using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>
/// Base element of any UI tree node
/// </summary>
public abstract class UIElement : IGridPlaceable
{
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
    public string Name { get; set; }
    public Color Background { get; set; } = Colors.Transparent;
    // IGridPlaceable
    public int Row { get; set; }
    public int Column { get; set; }

    internal Form? Owner { get; set; }

    public abstract void Draw(Graphics g);

    protected void Invalidate()
    {
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

    protected virtual void OnMouseOver()
    {
        // this.Draw(new Graphics()); нужно прокинуть сюда graphics
    }

    protected virtual void OnMouseLeave()
    {

    }

    protected virtual void OnMouseDown()
    {

    }

    protected virtual void OnMouseUp()
    {

    }

    internal void RaiseMouseOver() => OnMouseOver();
    internal void RaiseMouseLeave() => OnMouseLeave();
    internal void RaiseMouseDown() => OnMouseDown();
    internal void RaiseMouseUp() => OnMouseUp();
}
