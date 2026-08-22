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

    public Panel() => Size = new Size(200, 100);

    public override void Draw(Graphics g)
    {
        if (Background.A > 0)
            g.FillRectangle(new Rectangle(0, 0, Size.Width, Size.Height), Background);
        if (this.BorderWidth > 0)
        {
            g.DrawRectangle(this.LocalBounds, this.BorderColor, this.BorderWidth);
        }
    }

    protected override void ArrangeChildren()
    {
        // Panel — canvas-style контейнер, авто-расстановки нет,
        // поэтому Margin/Padding детей здесь намеренно не применяются.
        return;
    }
}