using ZeppelinForms.Core;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Input.Mouse;

public sealed record class MouseClickEventArgs(
    MouseButton Button,
    MouseButtonState State,
    Point Location) : ZfEventArgs
{
    public bool Handled { get; set; }
}
