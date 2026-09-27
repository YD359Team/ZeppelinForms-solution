using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Input.Gestures;

namespace ZeppelinForms.Input.Pointer;

/// <summary>A live contact: a finger on the screen, a pressed mouse button,
/// a pen. Exists from the press until the release or a cancel.</summary>
public sealed class PointerContact
{
    public required int Id { get; init; }
    public required PointerKind Kind { get; init; }

    /// <summary>The primary contact raises the compatibility mouse events.</summary>
    public required bool IsPrimary { get; init; }

    public required Point DownLocation { get; init; }
    public required long DownTimestamp { get; init; }

    /// <summary>The held buttons. For touch and pen always Left.</summary>
    public PointerButtons Buttons { get; set; }

    public Point Location { get; private set; }
    public long Timestamp { get; private set; }

    /// <summary>The element the press landed on.</summary>
    public UIElement? Pressed { get; set; }

    /// <summary>The chain from the root to <see cref="Pressed"/>, taken at the
    /// moment of the press and not recomputed after that.</summary>
    /// <remarks>
    /// It must not be recomputed from the current point: the layout may have
    /// shifted while the contact was held, and an ancestor that followed the
    /// press through the preview must get both the release and the cancel —
    /// even if the finger has long left its bounds.
    /// </remarks>
    public UIElement[] Chain { get; set; } = [];

    /// <summary>The element that took this contact for itself until the release.</summary>
    public UIElement? Capture { get; set; }

    /// <summary>Where the contact's events go: the capturing element, otherwise the pressed one.</summary>
    public UIElement? Target => Capture ?? Pressed;

    /// <summary>How far the contact has moved from the press point. The basis of
    /// the threshold for breaking into a pan: before it the interaction belongs
    /// to the descendant, after it an ancestor may claim it.</summary>
    public float TravelDistance => Point.DistanceBetween(DownLocation, Location);

    public long Duration => Timestamp - DownTimestamp;

    /// <summary>Created only by the input pipeline: a contact can't be invented
    /// from outside, otherwise the arena and the capture would drift apart
    /// from reality.</summary>
    internal PointerContact() { }

    /// <summary>The compatibility mouse events of this contact are cancelled —
    /// the winning recognizer drives it. The contact itself is alive.</summary>
    internal bool IsCompatCancelled { get; set; }

    internal GestureArena? Arena { get; set; }

    public void Update(Point location, long timestamp)
    {
        Location = location;
        Timestamp = timestamp;
    }
}