using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Animation;

/// <summary>
/// A rule: how a property gets to a new value. The assignment becomes
/// the target, and the visible value travels to it over the transition's duration.
/// </summary>
/// <remarks>
/// The assignment itself is instant: code, bindings and PropertyGrid read
/// the target, not a halfway value. The intermediate value lives in a separate
/// layer that drawing reads. This is how Core Animation, CSS and Compose work,
/// and it is exactly this separation that makes it possible some day to move
/// transitions to the render thread without touching the model.
/// </remarks>
public sealed class Transition
{
    private Transition(StyledProperty property, TimeSpan duration, Func<float, float> easing)
    {
        Property = property;
        Duration = duration;
        Easing = easing;
    }

    public StyledProperty Property { get; }

    public TimeSpan Duration { get; }

    public Func<float, float> Easing { get; }

    /// <summary>A transition with a duration and a curve.</summary>
    public static Transition Ease(
        StyledProperty property,
        TimeSpan duration,
        Func<float, float>? easing = null) =>
        new(property, duration, easing ?? Animation.Easing.EaseOut);

    /// <summary>The same in milliseconds — shorter to write in a control's markup.</summary>
    public static Transition Ease(
        StyledProperty property,
        int durationMs,
        Func<float, float>? easing = null) =>
        Ease(property, TimeSpan.FromMilliseconds(durationMs), easing);
}