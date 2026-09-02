using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Core;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Input.Mouse;

public sealed record class MouseMoveEventArgs(Point Location) : ZfEventArgs
{
    /// <summary>Элемент, с которого курсор ушёл, либо на который перешёл.
    /// null — курсор пришёл извне окна или вышел за его пределы.</summary>
    public object? RelatedElement { get; init; }

    public bool Handled { get; set; }
}