namespace ZeppelinForms.Forms.Interfaces;

/// <summary>
/// Elements with focus and input
/// </summary>
public interface IInputElement
{
    bool IsFocused { get; set; }
    bool TabStop { get; set; }
    uint TabIndex { get; set; }
}
