using System.Collections.ObjectModel;
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

    public abstract void Draw(Graphics g);
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
    public UIElement? Child { get; 
        set 
        {
            field = value;
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

    private void Children_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
        {
            foreach (UIElement item in e.NewItems!)
            {
                item.Parent = this;
            }
        }
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
