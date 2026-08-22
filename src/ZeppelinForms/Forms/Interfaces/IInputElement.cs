using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Interfaces;

/// <summary>
/// Elements with focus and input
/// </summary>
public interface IInputElement
{
    public event EventHandler<MouseClickEventArgs>? Click;

    bool IsFocused { get; set; }
    bool TabStop { get; set; }
    uint TabIndex { get; set; }
}