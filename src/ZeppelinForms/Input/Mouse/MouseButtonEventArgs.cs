using ZeppelinForms.Core;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.Input.Mouse;

/// <summary>Нажатие или отпускание конкретной кнопки мыши.</summary>
public sealed record class MouseButtonEventArgs(
    MouseButton Button,
    MouseButtonState State,
    Point Location,
    KeyModifiers Modifiers = KeyModifiers.None) : ZfEventArgs
{
    public bool Handled { get; set; }
}