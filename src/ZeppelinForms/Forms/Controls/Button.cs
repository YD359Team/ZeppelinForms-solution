using System.Xml.Linq;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Helpers;
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

    private bool _isHovered;

    protected override void OnMouseOver()
    {
        _isHovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave()
    {
        _isHovered = false;
        Invalidate();
    }

    public override void Draw(Graphics g)
    {
        ControlDrawing.DrawButton(g, this.LocalBounds, this.ButtonStyle, _isHovered, this.FillColor, this.Background, this.Text);
        if (this.BorderWidth > 0)
        {
            ControlDrawing.DrawBorder(g, this.LocalBounds, this.BorderColor, this.BorderWidth);
        }
    }
}

public enum ButtonStyle : byte
{
    Primary = 0,
    Secondary = 1
}
