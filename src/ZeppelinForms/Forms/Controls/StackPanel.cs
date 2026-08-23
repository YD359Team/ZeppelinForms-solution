using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Controls;

public class StackPanel : PanelControl
{
    public Orientation Orientation { get; set; } = Orientation.Vertical;
    public float Spacing { get; set; }

    public override void Draw(Graphics g) { }

    protected override Size MeasureOverride(Size availableSize)
    {
        var inner = new Size(
            Math.Max(0, availableSize.Width - Padding.Horizontal),
            Math.Max(0, availableSize.Height - Padding.Vertical));

        float mainAxisTotal = 0;
        float crossAxisMax = 0;

        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;
            var m = child.Margin;

            Size childAvailable = Orientation == Orientation.Vertical
                ? new Size(Math.Max(0, inner.Width - m.Horizontal), float.PositiveInfinity)
                : new Size(float.PositiveInfinity, Math.Max(0, inner.Height - m.Vertical));

            child.Measure(childAvailable);

            if (Orientation == Orientation.Vertical)
            {
                mainAxisTotal += child.DesiredSize.Height + m.Vertical + Spacing;
                crossAxisMax = Math.Max(crossAxisMax, child.DesiredSize.Width + m.Horizontal);
            }
            else
            {
                mainAxisTotal += child.DesiredSize.Width + m.Horizontal + Spacing;
                crossAxisMax = Math.Max(crossAxisMax, child.DesiredSize.Height + m.Vertical);
            }
        }

        Size content = Orientation == Orientation.Vertical
            ? new Size(crossAxisMax, mainAxisTotal)
            : new Size(mainAxisTotal, crossAxisMax);

        content = new Size(content.Width + Padding.Horizontal, content.Height + Padding.Vertical);

        return ResolveSize(content, availableSize);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var content = new Rectangle(
            new Point(Padding.Left, Padding.Top),
            new Size(
                Math.Max(0, finalSize.Width - Padding.Horizontal),
                Math.Max(0, finalSize.Height - Padding.Vertical)));

        float offset = Orientation == Orientation.Vertical ? content.Y : content.X;

        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;
            var m = child.Margin;

            if (Orientation == Orientation.Vertical)
            {
                offset += m.Top;
                var rect = new Rectangle(
                    new Point(content.X + m.Left, offset),
                    new Size(Math.Max(0, content.Width - m.Horizontal), child.DesiredSize.Height));
                child.Arrange(rect);
                offset += child.Size.Height + m.Bottom + Spacing;
            }
            else
            {
                offset += m.Left;
                var rect = new Rectangle(
                    new Point(offset, content.Y + m.Top),
                    new Size(child.DesiredSize.Width, Math.Max(0, content.Height - m.Vertical)));
                child.Arrange(rect);
                offset += child.Size.Width + m.Right + Spacing;
            }
        }

        return finalSize;
    }
}
