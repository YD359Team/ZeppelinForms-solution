using ZeppelinForms.Core;

namespace ZeppelinForms.Input.Keyboard;

/// <summary>A character typed into the window — after the keyboard layout,
/// dead keys and the input method have done their work.</summary>
public sealed record class TextInputEventArgs(char Character) : ZfEventArgs
{
    /// <summary>Set by a handler that took the character: it doesn't reach
    /// the focused element.</summary>
    public bool Handled { get; set; }
}