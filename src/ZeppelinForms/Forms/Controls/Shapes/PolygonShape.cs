using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Forms.Controls.Shapes;

public class PolygonShape : Shape
{
    /// <summary>Точки в долях от размера контрола (0..1).</summary>
    public List<Point> Points { get; init; } = [];

    public override void Draw(Graphics g)
    {
        if (Points.Count < 2 || !HasStroke) return;

        Rectangle bounds = StrokeAwareBounds;

        Point[] absolute = new Point[Points.Count + 1];

        for (int i = 0; i < Points.Count; i++)
            absolute[i] = new Point(
                bounds.X + bounds.Width * Points[i].X,
                bounds.Y + bounds.Height * Points[i].Y);

        absolute[^1] = absolute[0];   // замыкаем контур

        g.DrawPolyline(absolute, Stroke, StrokeThickness);
    }
}