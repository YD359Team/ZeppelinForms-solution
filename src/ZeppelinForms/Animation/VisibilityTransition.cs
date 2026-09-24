using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Animation;

/// <summary>
/// How an element appears and disappears: which look it comes from
/// and which look it leaves into. The look is opacity, scale and offset
/// from its own place.
/// </summary>
/// <remarks>
/// One description serves both directions: appearing travels from the given
/// look to the normal one, disappearing — from the normal look to the same one.
/// So a row that slid in from below also leaves downward, and not somewhere else.
///
/// All of this is draw-time properties: appearing does not push the neighbours,
/// and an exiting element is already removed from the layout by the time the
/// animation starts. The neighbours immediately take its place, and if the panel
/// has a LayoutTransition, they slide smoothly into the gap.
/// </remarks>
public sealed class VisibilityTransition
{
    private VisibilityTransition(
        TimeSpan duration,
        Func<float, float> easing,
        float opacity,
        float scale,
        float offsetX,
        float offsetY)
    {
        Duration = duration;
        Easing = easing;
        Opacity = opacity;
        Scale = scale;
        OffsetX = offsetX;
        OffsetY = offsetY;

        ForOpacity = Transition.Ease(UIElement.OpacityProperty, duration, easing);
        ForScaleX = Transition.Ease(UIElement.ScaleXProperty, duration, easing);
        ForScaleY = Transition.Ease(UIElement.ScaleYProperty, duration, easing);
        ForTranslateX = Transition.Ease(UIElement.TranslateXProperty, duration, easing);
        ForTranslateY = Transition.Ease(UIElement.TranslateYProperty, duration, easing);
    }

    public TimeSpan Duration { get; }

    public Func<float, float> Easing { get; }

    /// <summary>Opacity in the invisible state. 0 — fully transparent.</summary>
    public float Opacity { get; }

    /// <summary>Scale in the invisible state. 1 — no scaling.</summary>
    public float Scale { get; }

    /// <summary>Offset from its own place in the invisible state.</summary>
    public float OffsetX { get; }

    public float OffsetY { get; }

    // the rules for appearing are prepared once: it happens on every row
    // insertion, and there is no point creating five objects per row
    internal Transition ForOpacity { get; }
    internal Transition ForScaleX { get; }
    internal Transition ForScaleY { get; }
    internal Transition ForTranslateX { get; }
    internal Transition ForTranslateY { get; }

    /// <summary>Fading in from transparency.</summary>
    public static VisibilityTransition Fade(int durationMs, Func<float, float>? easing = null) =>
        new(TimeSpan.FromMilliseconds(durationMs), easing ?? Animation.Easing.EaseOut, 0f, 1f, 0f, 0f);

    /// <summary>Fading in with a slight scale-up — for cards and popups.</summary>
    public static VisibilityTransition FadeScale(
        int durationMs,
        float scale = 0.92f,
        Func<float, float>? easing = null) =>
        new(TimeSpan.FromMilliseconds(durationMs), easing ?? Animation.Easing.EaseOut, 0f, scale, 0f, 0f);

    /// <summary>Fading in with an offset — for list rows and notifications.</summary>
    public static VisibilityTransition Slide(
        int durationMs,
        float offsetX,
        float offsetY,
        Func<float, float>? easing = null) =>
        new(TimeSpan.FromMilliseconds(durationMs), easing ?? Animation.Easing.EaseOut, 0f, 1f, offsetX, offsetY);
}