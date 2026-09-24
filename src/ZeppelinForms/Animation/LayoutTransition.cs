using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Animation;

/// <summary>
/// How an element travels when the layout has put it in a new place.
/// </summary>
/// <remarks>
/// Works by the FLIP rule: the element already stands where the layout decided,
/// but is drawn from its old place and travels to a zero offset. What moves
/// is not the layout but the draw-time offset — so the travel costs the
/// neighbours nothing, hit testing follows the picture all the time, and an
/// interrupted travel continues from wherever it was caught.
///
/// It also follows that controls need to know nothing about animation:
/// expanding a tree node, WrapPanel reflowing when the window width changes,
/// and inserting into a DragList are all a change of position in Arrange,
/// that is, one and the same event.
/// </remarks>
public sealed class LayoutTransition
{
    private readonly TimeSpan? _fixedDuration;
    private readonly float _speed;
    private readonly TimeSpan _minDuration;
    private readonly TimeSpan _maxDuration;

    private readonly Transition? _fixedX;
    private readonly Transition? _fixedY;

    private LayoutTransition(TimeSpan duration, Func<float, float> easing)
    {
        _fixedDuration = duration;
        Easing = easing;

        _fixedX = Transition.Ease(UIElement.TranslateXProperty, duration, easing);
        _fixedY = Transition.Ease(UIElement.TranslateYProperty, duration, easing);
    }

    private LayoutTransition(float speed, TimeSpan min, TimeSpan max, Func<float, float> easing)
    {
        _speed = speed;
        _minDuration = min;
        _maxDuration = max;
        Easing = easing;
    }

    public Func<float, float> Easing { get; }

    /// <summary>A constant duration, however far the element travels.</summary>
    /// <remarks>
    /// Suitable when distances are roughly equal. When they differ,
    /// a constant duration lies: a row that moved by one row's height
    /// crawls for the same two tenths of a second as one that crossed
    /// the whole screen, and it looks unnaturally slow.
    /// </remarks>
    public static LayoutTransition Ease(TimeSpan duration, Func<float, float>? easing = null) =>
        new(duration, easing ?? Animation.Easing.EaseOut);

    public static LayoutTransition Ease(int durationMs, Func<float, float>? easing = null) =>
        Ease(TimeSpan.FromMilliseconds(durationMs), easing);

    /// <summary>Duration by distance: the farther to travel, the longer it takes.
    /// Bounds are needed on both sides — otherwise a short move becomes
    /// a flicker and a long one drags on.</summary>
    public static LayoutTransition Speed(
        float pixelsPerSecond = 1600f,
        int minDurationMs = 90,
        int maxDurationMs = 320,
        Func<float, float>? easing = null) =>
        new(
            pixelsPerSecond,
            TimeSpan.FromMilliseconds(minDurationMs),
            TimeSpan.FromMilliseconds(maxDurationMs),
            easing ?? Animation.Easing.EaseOut);

    /// <summary>The rule for one axis, taking into account how far to travel along it.</summary>
    internal Transition For(StyledProperty<float> property, float distance)
    {
        // the constant duration is prepared once: travel happens on every
        // list rearrangement, and there is no point creating objects per row
        if (_fixedDuration is not null)
            return ReferenceEquals(property, UIElement.TranslateXProperty) ? _fixedX! : _fixedY!;

        double milliseconds = MathF.Abs(distance) / _speed * 1000f;

        var duration = TimeSpan.FromMilliseconds(Math.Clamp(
            milliseconds, _minDuration.TotalMilliseconds, _maxDuration.TotalMilliseconds));

        return Transition.Ease(property, duration, Easing);
    }
}