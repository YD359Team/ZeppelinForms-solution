using ZeppelinForms.Core;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Input.Mouse;

public sealed record class MouseWheelEventArgs(
    Point Location,
    int Delta) : ZfEventArgs
{
    public bool Handled { get; set; }
}
