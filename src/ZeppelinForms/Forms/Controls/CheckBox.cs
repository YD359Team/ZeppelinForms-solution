using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

public class CheckBox : UnitControl, ITextElement, IInputElement
{
    public bool IsChecked { get; set; }
    // ITextElement
    public string? Text { get; set; }
    public HorizontalAlign HorizontalAlign { get; set; }
    public VerticalAlign VerticalAlign { get; set; }
    public Color TextColor { get; set; } = Colors.White;
    // IInputElement
    public bool IsFocused { get; set; }
    public bool TabStop { get; set; } = true;
    public uint TabIndex { get; set; }

    public override void Draw(Graphics g)
    {
        throw new NotImplementedException();
    }
}