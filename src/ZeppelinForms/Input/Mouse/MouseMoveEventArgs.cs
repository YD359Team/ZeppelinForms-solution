using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Core;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Input.Mouse;

public sealed record class MouseMoveEventArgs(Point Location) : ZfEventArgs
{
    /// <summary>The element the cursor left, or the one it moved to.
    /// null — the cursor came from outside the window or went beyond it.</summary>
    public object? RelatedElement { get; init; }

    public bool Handled { get; set; }
}