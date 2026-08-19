using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms.Controls;

public class Button : UnitControl, IInputElement, IBorderedElement
{
    public string? Text { get; set; }
    // IBorderedElement
    public Color BorderColor { get; set; } = Colors.Black;
    public float BorderWidth { get; set; } = 1f;
    // IInputElement
    public bool IsFocused { get; set; }

    public override void Draw(Graphics g)
    {
        if (this.BorderWidth > 0)
        {
            g.DrawRectangle(this.LocalBounds, this.BorderColor, this.BorderWidth);
        }
        if (Text is not null)
            g.DrawText(Text, this.ContentBounds, Colors.Black);
    }
}
