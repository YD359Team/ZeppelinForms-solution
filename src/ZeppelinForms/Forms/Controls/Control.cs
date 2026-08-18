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
    public Size Size { get; set; }

    public string Name { get; set; }

    internal Form? Owner { get; set; }

    public abstract void Draw(Graphics g);


    protected void Invalidate()
    {
        UIElement root = this;
        while (root.Parent is not null)
            root = root.Parent;

        root.Owner?.Invalidate();
    }
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
public class Panel : PanelControl
{
    public Color Background { get; set; } = Colors.Transparent;

    public override void Draw(Graphics g)
    {
        if (Background.A > 0)
            g.FillRectangle(new Rectangle(0, 0, Size.Width, Size.Height), Background);
    }
}

/// <summary>
/// Control with caption
/// </summary>
public class Label : UnitControl
{
    public string? Text { get; set; }

    public override void Draw(Graphics g)
    {
        if (Text is not null)
            g.DrawText(Text, Point.Empty, Colors.Black);
    }
}
