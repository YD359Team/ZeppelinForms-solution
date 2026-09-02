using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Forms.Controls.Shapes;

public class LineShape : Shape
{
    /// <summary>Начало и конец в долях от размера контрола (0..1),
    /// чтобы линия масштабировалась вместе с ним.</summary>
    public Point From { get; set; } = new(0, 0);
    public Point To { get; set; } = new(1, 1);

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