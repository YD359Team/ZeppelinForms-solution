using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Forms.Controls;

/// <summary>How much room the one asking has.</summary>
/// <remarks>
/// A layout area is classified, not the screen: a panel 300 units wide
/// inside a wide window is exactly as cramped as a whole window on a phone,
/// and it should behave the same way.
/// </remarks>
public enum SizeClass
{
    /// <summary>A phone, a narrow column, a slide-out panel.</summary>
    Compact,

    /// <summary>A tablet, half a window.</summary>
    Medium,

    /// <summary>A full-screen desktop window.</summary>
    Expanded,
}

/// <summary>Size class boundaries. One set per application — that is the whole
/// point: thresholds scattered across screens cost more to reconcile later
/// than to define up front.</summary>
public static class Breakpoints
{
    /// <summary>Below this width — Compact. In logical units.</summary>
    public static float Medium { get; set; } = 600f;

    /// <summary>From this width — Expanded.</summary>
    public static float Expanded { get; set; } = 1000f;

    public static SizeClass Classify(float width) =>
        width < Medium ? SizeClass.Compact
        : width < Expanded ? SizeClass.Medium
        : SizeClass.Expanded;

    /// <summary>Classification by width. Height deliberately takes no part:
    /// it distinguishes landscape orientation at most, which is a separate
    /// question, and mixing it with crampedness means six states
    /// instead of three.</summary>
    public static SizeClass Classify(Size size) => Classify(size.Width);
}