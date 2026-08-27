using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms.Controls;

public class Border : WrapControl, IBorderedElement
{
    // IBorderedElement
    public Color BorderColor { get; set; } = Colors.Black;
    public float BorderWidth { get; set; } = 0f;

    public override void Draw(Graphics g)
    {
        if (Background.A > 0)
            g.FillRectangle(this.LocalBounds, Background);

        if (this.BorderWidth > 0)
            g.DrawRectangle(this.LocalBounds, this.BorderColor, this.BorderWidth);
    }
}
