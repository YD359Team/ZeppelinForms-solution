using ZeppelinForms.Core;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.Input.Mouse;

/// <summary>A press or release of a specific mouse button.</summary>
public sealed record class MouseButtonEventArgs(
    MouseButton Button,
    MouseButtonState State,
    Point Location,
    KeyModifiers Modifiers = KeyModifiers.None) : ZfEventArgs
{
    public bool Handled { get; set; }
}