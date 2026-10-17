namespace ZeppelinForms.Animation;

/// <summary>
/// A callback on every frame with the time since the previous one. Runs until the
/// callback returns false or the animation is removed.
/// </summary>
/// <remarks>
/// For what the other animations don't fit: a viewport that renders a scene every
/// frame, a game loop, a playback driven by its own clock. Animate gives a value
/// over a duration and AnimateLoop a normalized phase; neither tells how much time
/// has passed, and a fixed-step simulation needs exactly that.
/// The step is the clock's: no larger than 100 ms after a stall, and no step at all
/// while the target is hidden — the loop pauses with its page.
/// </remarks>
internal sealed class FrameAnimation(object target, string key, Func<TimeSpan, bool> frame) : IAnimation
{
    public object Target { get; } = target;
    public string Key { get; } = key;

    public bool Advance(TimeSpan elapsed) => frame(elapsed);

    /// <summary>The loop has no final value; removal simply stops it.</summary>
    public void Cancel(bool applyFinalValue) { }
}