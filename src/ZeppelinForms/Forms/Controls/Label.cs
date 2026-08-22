using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Control with caption
/// </summary>
public class Label : UnitControl, ITextElement, IBorderedElement
{
    // ITextElement
    public string? Text { get; set; }
    public HorizontalAlign HorizontalAlign { get; set; }
    public VerticalAlign VerticalAlign { get; set; }
    public Color TextColor { get; set; } = Colors.White;
    // IBorderedElement
    public Color BorderColor { get; set; } = Colors.Black;
    public float BorderWidth { get; set; } = 0f;

    public Label() => Size = new Size(100, 23);

    public override void Draw(Graphics g)
    {
        if (this.BorderWidth > 0)
        {
            g.DrawRectangle(this.LocalBounds, this.BorderColor, this.BorderWidth);
        }
        if (Text is not null)
            g.DrawText(this.Text, this.ContentBounds, Colors.Black, this.HorizontalAlign, this.VerticalAlign);
    }
}
