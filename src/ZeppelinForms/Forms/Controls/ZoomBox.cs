using ZeppelinForms.Drawing;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Allow zoom in\out for child
/// </summary>
public class ZoomBox : WrapControl, IInputElement
{
    // IInputElement
    public event EventHandler<MouseClickEventArgs>? Click;
    public bool IsFocused { get; set; }
    public bool TabStop { get; set; }
    public uint TabIndex { get; set; }

    public override void Draw(Graphics g)
    {
        
    }

    public void Zoom(float factor)
    {
        // TODO: zoom child
    }
}
