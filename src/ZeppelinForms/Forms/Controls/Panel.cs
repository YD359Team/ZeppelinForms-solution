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
    // IBorderedElement
    public Color BorderColor { get; set; } = Colors.Black;
    public float BorderWidth { get; set; } = 0f;

    public override void Draw(Graphics g)
    {
        if (this.BorderWidth > 0)
        {
            g.DrawRectangle(this.LocalBounds, this.BorderColor, this.BorderWidth);
        }
        if (Background.A > 0)
            g.FillRectangle(new Rectangle(0, 0, Size.Width, Size.Height), Background);
    }

    protected override void ArrangeChildren()
    {
        // Panel — canvas-style контейнер, авто-расстановки нет,
        // поэтому Margin/Padding детей здесь намеренно не применяются.
        return;
    }
}
