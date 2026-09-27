using System;
using System.Collections.Generic;
using System.Text;

namespace ZeppelinForms.Input.Pointer;

/// <summary>The nature of a contact. The mouse differs from a finger not in
/// precision but in having the state "over the element, but not pressed":
/// touch has no such state, and code that doesn't tell them apart leaves
/// the highlight and the tooltip hanging after the release.</summary>
public enum PointerKind
{
    Mouse,
    Touch,
    Pen,
}