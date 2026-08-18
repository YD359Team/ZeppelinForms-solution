using System.Collections.ObjectModel;
using System.Collections.Specialized;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Base element of any UI tree node
/// </summary>
public abstract class UIElement
{
    public UIElement? Parent { get; internal set; }

    public Point Position { get; set; }
    private Size _size;
    public Size Size
    {
        get => _size;
        set
        {
            if (_size == value) return;
            _size = value;
            OnArrange();
        }
    }
    public Rectangle Rectangle => new(Position, Size);
    public Rectangle LocalBounds => new(Point.Empty, Size);
    public string Name { get; set; }
    public Color Background { get; set; } = Colors.Transparent;

    internal Form? Owner { get; set; }

    public abstract void Draw(Graphics g);
    protected virtual void OnArrange() { }

    protected void Invalidate()
    {
        UIElement root = this;
        while (root.Parent is not null)
            root = root.Parent;

        root.Owner?.Invalidate();
    }
}

/// <summary>
/// Elements with border
/// </summary>
public interface IBorderedElement
{
    Color BorderColor { get; set; }
    float BorderWidth { get; set; }
}

/// <summary>
/// Elements with focus and input
/// </summary>
public interface IInputElement
{
    bool IsFocused { get; set; }
}

/// <summary>
/// Control without children
/// </summary>
public abstract class UnitControl : UIElement
{

}

/// <summary>
/// Control with single child (or nothing)
/// </summary>
public abstract class SingleControl : UIElement
{
    public UIElement? Child
    {
        get;
        set
        {
            if (field == value)
                return;

            if (field is not null)
                field.Parent = null;

            field = value;

            if (value is not null)
                value.Parent = this;
        }
    }
}

/// <summary>
/// Control with any count of children
/// </summary>
public abstract class PanelControl : UIElement
{
    public ObservableCollection<UIElement> Children { get; set; } = [];

    public PanelControl()
    {
        this.Children.CollectionChanged += Children_CollectionChanged;
    }

    private void Children_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (UIElement item in e.OldItems)
                item.Parent = null;

        if (e.NewItems is not null)
            foreach (UIElement item in e.NewItems)
                item.Parent = this;
    }
}

/// <summary>
/// Simple panel
/// </summary>
public class Panel : PanelControl, IBorderedElement
{
    // IBorderedElement
    public Color BorderColor { get; set; } = Colors.Black;
    public float BorderWidth { get; set; } = 0f;

    public override void Draw(Graphics g)
    {
        if (this.BorderWidth > 0)
        {
            g.DrawRectangle(this.LocalBounds, this.BorderColor, this.BorderWidth);
        }
        if (Background.A > 0)
            g.FillRectangle(new Rectangle(0, 0, Size.Width, Size.Height), Background);
    }
}

/// <summary>
/// Control with caption
/// </summary>
public class Label : UnitControl, IBorderedElement
{
    public string? Text { get; set; }
    // IBorderedElement
    public Color BorderColor { get; set; } = Colors.Black;
    public float BorderWidth { get; set; } = 0f;

    public override void Draw(Graphics g)
    {
        if (this.BorderWidth > 0)
        {
            g.DrawRectangle(this.LocalBounds, this.BorderColor, this.BorderWidth);
        }
        if (Text is not null)
            g.DrawText(this.Text, this.LocalBounds, Colors.Black);
    }
}

public class Button : UnitControl, IInputElement, IBorderedElement
{
    public string? Text { get; set; }
    // IBorderedElement
    public Color BorderColor { get; set; } = Colors.Black;
    public float BorderWidth { get; set; } = 1f;
    // IInputElement
    public bool IsFocused { get; set; }

    public override void Draw(Graphics g)
    {
        if (this.BorderWidth > 0)
        {
            g.DrawRectangle(this.LocalBounds, this.BorderColor, this.BorderWidth);
        }
        if (Text is not null)
            g.DrawText(Text, this.LocalBounds, Colors.Black);
    }
}
