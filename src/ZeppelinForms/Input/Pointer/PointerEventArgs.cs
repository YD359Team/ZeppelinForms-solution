using ZeppelinForms.Core;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Input.Pointer;

public sealed record class PointerEventArgs(
    int PointerId,
    PointerKind Kind,
    Point Location,
    MouseButton Button = MouseButton.Left,
    float Pressure = 1f,
    KeyModifiers Modifiers = KeyModifiers.None) : ZfEventArgs
{
    /// <summary>The moment of the event by <see cref="Environment.TickCount64"/>.
    /// Set by the platform, not by the receiver: time passes between the event
    /// arriving from the system and its processing, and a velocity computed from
    /// the processing time is the less truthful the more the frame stutters —
    /// and swipe and fling need the velocity.</summary>
    public long Timestamp { get; init; } = Environment.TickCount64;

    /// <summary>The primary contact — the one the compatibility mouse events are
    /// raised from. The first to touch; when it is released, the primary is not
    /// reassigned, otherwise a control would get the press from one finger
    /// and the release from another.</summary>
    public bool IsPrimary { get; init; } = true;

    /// <summary>The size of the contact patch in logical units. Zero for the mouse.
    /// Needed where a finger hit is wider than a point: an enlarged press zone
    /// for small elements.</summary>
    public Size ContactSize { get; init; }

    public bool Handled { get; set; }
}