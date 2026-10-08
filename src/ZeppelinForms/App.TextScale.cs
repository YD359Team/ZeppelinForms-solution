namespace ZeppelinForms;

/// <summary>The system's text scale: "Text size" on Windows, "Font size" on Android.</summary>
/// <remarks>
/// <para>
/// Opt-in in 0.13: an application calls <see cref="UseSystemTextScale"/>. Existing
/// layouts have controls of fixed height, and text that grows by itself would be cut
/// in them; the accessibility audit of the inspector shows such places, and the
/// default may change once applications have had the chance to fix them.
/// </para>
/// <para>
/// The scale multiplies the size of every font — in UIElement.EffectiveFont — and
/// nothing else: icons, paddings and strokes stay. That is the difference from the
/// display scale (DPI), which enlarges everything: here only text grows, as the
/// user asked, and the layout makes room for it.
/// </para>
/// </remarks>
public partial class App
{
    private static bool s_followTextScale;

    /// <summary>The scale applied to every font now: the system's while
    /// <see cref="UseSystemTextScale"/> is on, otherwise 1.</summary>
    public static float TextScale { get; private set; } = 1f;

    /// <summary>The text scale changed: forms measure everything again.</summary>
    public static event EventHandler? TextScaleChanged;

    /// <summary>Take the system's text scale, and follow its changes.</summary>
    /// <param name="enabled">False — back to the unscaled size.</param>
    public static void UseSystemTextScale(bool enabled = true)
    {
        s_followTextScale = enabled;
        ApplyTextScale();
    }

    /// <summary>Whether the system's text scale is followed.</summary>
    public static bool IsFollowingSystemTextScale => s_followTextScale;

    /// <summary>A text scale set by a design tool rather than read from the system:
    /// the previewer shows a view at 150 % text without touching the machine's
    /// settings. Null gives the scale back to <see cref="UseSystemTextScale"/>.</summary>
    internal static void OverrideTextScale(float? scale)
    {
        s_textScaleOverride = scale is { } value ? Math.Clamp(value, 1f, 3f) : null;
        ApplyTextScale();
    }

    private static float? s_textScaleOverride;

    /// <summary>Put the scale in place: the system's while following, otherwise 1.
    /// Kept between 1 and 3 — a system never asks for less, and a scale out of that
    /// range is a broken setting rather than a wish.</summary>

    private static void ApplyTextScale()
    {
        if (s_textScaleOverride is { } overridden)
        {
            if (overridden == TextScale) return;

            TextScale = overridden;
            TextScaleChanged?.Invoke(null, EventArgs.Empty);
            return;
        }

        float scale = s_followTextScale && s_appearance is { } appearance
            ? Math.Clamp(appearance.TextScale, 1f, 3f)
            : 1f;

        if (scale == TextScale) return;

        TextScale = scale;
        TextScaleChanged?.Invoke(null, EventArgs.Empty);
    }
}