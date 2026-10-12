using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Core;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Input.Mouse;

public sealed record class MouseMoveEventArgs(Point Location) : ZfEventArgs
{
    /// <summary>The element the cursor left, or the one it moved to.
    /// null — the cursor came from outside the window or went beyond it.</summary>
    public object? RelatedElement { get; init; }

    /// <summary>The modifier keys held during the move, as MouseButtonEventArgs
    /// carries them for a press. A drag that changes with Shift or Ctrl reads them
    /// here instead of the Keyboard snapshot, which the window updates only from
    /// its own key events.</summary>
    public KeyModifiers Modifiers { get; init; }

    /// <summary>The buttons held during the move. None is hover; Left is a drag.
    /// For touch and pen a live contact is always Left.</summary>
    public PointerButtons Buttons { get; init; }

    public bool Handled { get; set; }
}