using ZeppelinForms.Core;

namespace ZeppelinForms.Input.Keyboard;

public sealed record class KeyEventArgs(
    Key Key,
    KeyModifiers Modifiers = KeyModifiers.None,
    bool IsRepeat = false) : ZfEventArgs
{
    public bool Handled { get; set; }
}
