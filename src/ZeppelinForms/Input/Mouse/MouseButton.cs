using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Core;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Input.Mouse;

public enum MouseButton : byte
{
    Left = 0,
    Middle,
    Right,
}

public enum MouseButtonState : byte
{
    Down = 0,
    Up
}

public sealed record class MouseMoveEventArgs(
    Point Location) : ZfEventArgs;

public sealed record class MouseClickEventArgs(
    MouseButton Button, 
    MouseButtonState State, 
    Point Location) : ZfEventArgs;
