using System.Xml.Linq;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms.Controls;

public class Button : UnitControl, IInputElement, IBorderedElement
{
    public string? Text { get; set; }
    public Color FillColor { get; set; } = LightThemeColors.ButtonFill;
    public ButtonStyle ButtonStyle { get; set; } = ButtonStyle.Secondary;
    // IBorderedElement
    public Color BorderColor { get; set; } = LightThemeColors.ButtonFill;
    public float BorderWidth { get; set; } = 1f;
    // IInputElement
    public bool IsFocused { get; set; }
    public bool TabStop { get; set; }
    public uint TabIndex { get; set; }

    public override void Draw(Graphics g)
    {
        Color fore = (ButtonStyle == ButtonStyle.Secondary ? this.FillColor : this.Background);
        Color bg = (ButtonStyle == ButtonStyle.Secondary ? this.Background : this.FillColor);
        g.FillRectangle(this.LocalBounds, bg);
        if (this.BorderWidth > 0)
        {
            g.DrawRectangle(this.LocalBounds, fore, this.BorderWidth);
        }
        if (Text is not null)
            g.DrawText(Text, this.ContentBounds, fore);
    }
}

public enum ButtonStyle : byte
{
    Primary = 0,
    Secondary = 1
}
