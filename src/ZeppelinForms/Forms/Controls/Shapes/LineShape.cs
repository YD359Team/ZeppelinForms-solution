using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Forms.Controls.Shapes;

public class LineShape : Shape
{
    /// <summary>Start and end as fractions of the control's size (0..1),
    /// so that the line scales with it.</summary>
    public Point From
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            InvalidateVisual();
        }
    } = new(0, 0);

    public Point To
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            InvalidateVisual();
        }
    } = new(1, 1);

    public override void Draw(Graphics g)
    {
        if (!HasStroke) return;

        Rectangle bounds = StrokeAwareBounds;

        g.DrawLine(
            new Point(bounds.X + bounds.Width * From.X, bounds.Y + bounds.Height * From.Y),
            new Point(bounds.X + bounds.Width * To.X, bounds.Y + bounds.Height * To.Y),
            Stroke, StrokeThickness);
    }

    protected override Size DefaultSize => new(64, 2);
}