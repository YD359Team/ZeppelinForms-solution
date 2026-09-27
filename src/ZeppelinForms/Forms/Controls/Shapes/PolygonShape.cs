using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Forms.Controls.Shapes;

public class PolygonShape : Shape
{
    /// <summary>Points as fractions of the control's size (0..1).</summary>
    public List<Point> Points { get; init; } = [];

    /// <remarks>
    /// Fill used to be ignored: only the outline was drawn, and a polygon with
    /// a fill but no stroke was not drawn at all.
    /// </remarks>
    public override void Draw(Graphics g)
    {
        if (Points.Count < 2 || (!HasFill && !HasStroke)) return;

        Rectangle bounds = StrokeAwareBounds;

        Point[] absolute = new Point[Points.Count + 1];

        for (int i = 0; i < Points.Count; i++)
            absolute[i] = new Point(
                bounds.X + bounds.Width * Points[i].X,
                bounds.Y + bounds.Height * Points[i].Y);

        absolute[^1] = absolute[0];   // close the outline

        // a polygon needs at least three corners to have an area; the fill goes
        // first so that the stroke lies on top of it, as in the other shapes
        if (HasFill && Points.Count >= 3)
            g.FillPolygon(absolute.AsSpan(0, Points.Count), Fill);

        if (HasStroke)
            g.DrawPolyline(absolute, Stroke, StrokeThickness);
    }
}