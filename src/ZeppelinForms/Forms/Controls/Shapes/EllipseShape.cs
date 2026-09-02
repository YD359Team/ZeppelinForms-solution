using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Forms.Controls.Shapes;

public class EllipseShape : Shape
{
    public override void Draw(Graphics g)
    {
        Rectangle bounds = StrokeAwareBounds;

        if (HasFill)
            g.FillEllipse(bounds, Fill);

        if (HasStroke)
            g.DrawEllipse(bounds, Stroke, StrokeThickness);
    }
}