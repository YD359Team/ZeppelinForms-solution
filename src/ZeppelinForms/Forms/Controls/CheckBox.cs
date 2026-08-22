using ZeppelinForms.Drawing;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

public class CheckBox : UnitControl, IInputElement
{
    public bool IsChecked { get; set; }
    public string? Text { get; set; }

    // IInputElement
    public event EventHandler<MouseClickEventArgs>? Click;
    public bool IsFocused { get; set; }
    public bool TabStop { get; set; } = true;
    public uint TabIndex { get; set; }

    public override void Draw(Graphics g)
    {
        throw new NotImplementedException();
    }
}