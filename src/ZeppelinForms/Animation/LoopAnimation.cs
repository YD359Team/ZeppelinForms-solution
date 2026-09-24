namespace ZeppelinForms.Animation;

/// <summary>
/// An endless loop: yields a phase from 0 to 1 and starts over.
/// Stops only when removed — it never ends by itself.
/// </summary>
public sealed class LoopAnimation : IAnimation
{
    private readonly Action<float> _apply;
    private readonly TimeSpan _period;

    private TimeSpan _elapsed;

    public object Target { get; }
    public string Key { get; }

    public LoopAnimation(object target, string key, TimeSpan period, Action<float> apply)
    {
        Target = target;
        Key = key;
        _period = period > TimeSpan.Zero ? period : TimeSpan.FromSeconds(1);
        _apply = apply;
    }

    public bool Advance(TimeSpan elapsed)
    {
        _elapsed += elapsed;

        // subtract in a loop rather than taking the modulus: after a stall
        // of several periods the phase still stays within one cycle
        while (_elapsed >= _period)
            _elapsed -= _period;

        _apply((float)(_elapsed / _period));

        return true;
    }

    /// <summary>An endless animation has neither a final value nor
    /// a completion handler — for it, removal is simply stopping.</summary>
    public void Cancel(bool applyFinalValue) { }
}