using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Helpers;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Simple panel
/// </summary>
public class Panel : PanelControl, IBorderedElement
{
    public Color BorderColor { get; set; } = Colors.Black;
    public float BorderWidth { get; set; } = 0f;

    public override void Draw(Graphics g)
    {
        if (Background.A > 0)
            g.FillRectangle(this.LocalBounds, Background);

        if (BorderWidth > 0)
            g.DrawRectangle(this.LocalBounds, BorderColor, BorderWidth);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Canvas-style: дети сами решают, какого они размера/где стоят,
        // Panel их не ужимает. Собственный размер Panel — либо явный (Size
        // задан), либо 0 — под bounding box детей не подстраивается,
        // задавайте Size явно, как и раньше.
        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;
            child.Measure(new Size(float.PositiveInfinity, float.PositiveInfinity));
        }

        return ResolveSize(Size.Empty, availableSize);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;
            child.Arrange(new Rectangle(child.Position, child.DesiredSize));
        }

        return finalSize;
    }
}
