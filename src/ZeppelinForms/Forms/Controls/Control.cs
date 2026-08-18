using System.Collections.ObjectModel;
using System.Collections.Specialized;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Base element of any UI tree node
/// </summary>
public abstract class UIElement
{
    public UIElement? Parent { get; internal set; }

    public Dock Docking { get; set; }
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

    protected abstract void ArrangeChildren();
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

    protected override void ArrangeChildren()
    {
        return;
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

public class StackPanel : PanelControl
{
    public Orientation Orientation { get; set; } = Orientation.Vertical;
    public float Spacing { get; set; }

    public override void Draw(Graphics g)
    {
        return;
    }

    protected override void ArrangeChildren()
    {
        float offset = 0;

        foreach (var child in Children)
        {
            if (Orientation == Orientation.Vertical)
            {
                child.Position = new Point(0, offset);
                child.Size = new Size(Size.Width, child.Size.Height); // растянуть по ширине
                offset += child.Size.Height + Spacing;
            }
            else
            {
                child.Position = new Point(offset, 0);
                child.Size = new Size(child.Size.Width, Size.Height);
                offset += child.Size.Width + Spacing;
            }
        }
    }
}

public readonly record struct GridLength(float Value, bool IsStar)
{
    public static GridLength Fixed(float px) => new(px, false);
    public static GridLength Star(float weight = 1) => new(weight, true);
}

public class Grid : PanelControl
{
    public List<GridLength> RowDefinitions { get; } = [];
    public List<GridLength> ColumnDefinitions { get; } = [];

    protected override void ArrangeChildren()
    {
        float[] rowHeights = ResolveSizes(RowDefinitions, Size.Height);
        float[] colWidths = ResolveSizes(ColumnDefinitions, Size.Width);

        foreach (var child in Children)
        {
            var (row, col) = child is IGridPlaceable p ? (p.Row, p.Column) : (0, 0);

            child.Position = new Point(colWidths.Take(col).Sum(), rowHeights.Take(row).Sum());
            child.Size = new Size(colWidths[col], rowHeights[row]);
        }
    }

    private static float[] ResolveSizes(List<GridLength> defs, float total)
    {
        float fixedSum = defs.Where(d => !d.IsStar).Sum(d => d.Value);
        float starSum = defs.Where(d => d.IsStar).Sum(d => d.Value);
        float remaining = Math.Max(0, total - fixedSum);

        return defs.Select(d => d.IsStar
            ? (starSum > 0 ? remaining * (d.Value / starSum) : 0)
            : d.Value).ToArray();
    }

    public override void Draw(Graphics g)
    {
        return;
    }
}
