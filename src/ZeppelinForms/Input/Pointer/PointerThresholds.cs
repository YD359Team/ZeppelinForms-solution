using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Input.Pointer;

/// <summary>Recognition thresholds in millimeters and their conversion
/// to the logical units of a specific screen.</summary>
/// <remarks>
/// The numbers are in millimeters, not pixels, deliberately. A finger covers
/// a patch of about 8 mm and trembles by 2–3 mm even during a still press;
/// the mouse doesn't tremble at all. A threshold tuned on the desktop in pixels
/// will turn out on a phone either indistinguishable from zero or impassable.
/// </remarks>
public static class PointerThresholds
{
    /// <summary>The tremble that still counts as a press at one point.
    /// The mouse has its own value: it has no tremble, and a large tolerance
    /// would only eat short drags.</summary>
    public const float TouchTapSlopMm = 2.5f;
    public const float MouseTapSlopMm = 0.6f;

    /// <summary>The break threshold: before it the contact belongs to the
    /// descendant, after it an ancestor with a pan may fight for it.</summary>
    public const float TouchDragSlopMm = 3.5f;
    public const float MouseDragSlopMm = 1f;

    /// <summary>The minimum path after which a movement counts as a swipe
    /// rather than a miss past the press.</summary>
    public const float SwipeMinDistanceMm = 12f;

    /// <summary>Holding without movement — until the long press fires.</summary>
    public const int LongPressMs = 500;

    /// <summary>Longer than this is no longer a tap, even if the finger didn't move.</summary>
    public const int TapMaxDurationMs = 300;

    public static float TapSlop(PointerKind kind, DisplayInfo display) =>
        display.MillimetersToLogical(kind == PointerKind.Mouse ? MouseTapSlopMm : TouchTapSlopMm);

    public static float DragSlop(PointerKind kind, DisplayInfo display) =>
        display.MillimetersToLogical(kind == PointerKind.Mouse ? MouseDragSlopMm : TouchDragSlopMm);

    public static float SwipeMinDistance(DisplayInfo display) =>
        display.MillimetersToLogical(SwipeMinDistanceMm);
}