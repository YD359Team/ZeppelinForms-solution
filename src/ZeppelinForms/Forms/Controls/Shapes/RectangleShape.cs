using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Forms.Controls.Shapes;

public class RectangleShape : Shape
{
    public CornerRadius Radius { get; set; } = CornerRadius.Zero;

    public override void Draw(Graphics g)
    {
        Rectangle bounds = StrokeAwareBounds;

        if (HasFill)
            g.FillRoundRectangle(bounds, Radius, Fill);

        if (HasStroke)
            g.DrawRoundRectangle(bounds, Radius, Stroke, StrokeThickness);
    }
}
