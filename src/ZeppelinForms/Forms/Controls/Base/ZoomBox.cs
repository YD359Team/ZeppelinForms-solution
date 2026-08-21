using ZeppelinForms.Drawing;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>
/// Allow zoom in\out for child
/// </summary>
public class ZoomBox : UIElement, IInputElement
{
    // IInputElement
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
