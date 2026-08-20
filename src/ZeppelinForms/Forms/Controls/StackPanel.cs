using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Controls;

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
        var content = ContentBounds;
        float offset = Orientation == Orientation.Vertical ? content.Y : content.X;

        foreach (var child in Children)
        {
            if (!child.IsVisible)
                continue;

            var m = child.Margin;

            if (Orientation == Orientation.Vertical)
            {
                offset += m.Top;
                child.Position = new Point(content.X + m.Left, offset);
                child.Size = new Size(Math.Max(0, content.Width - m.Horizontal), child.Size.Height);
                offset += child.Size.Height + m.Bottom + Spacing;
            }
            else
            {
                offset += m.Left;
                child.Position = new Point(offset, content.Y + m.Top);
                child.Size = new Size(child.Size.Width, Math.Max(0, content.Height - m.Vertical));
                offset += child.Size.Width + m.Right + Spacing;
            }
        }
    }
}
