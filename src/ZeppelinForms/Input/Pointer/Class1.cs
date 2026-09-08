using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Core;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Input.Pointer;

public enum PointerKind { Mouse, Touch, Pen }

public sealed record class PointerEventArgs(
    int PointerId,
    PointerKind Kind,
    Point Location,
    MouseButton Button = MouseButton.Left,
    float Pressure = 1f,
    KeyModifiers Modifiers = KeyModifiers.None) : ZfEventArgs;
