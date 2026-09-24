namespace ZeppelinForms.Animation;

public interface IAnimation
{
    object Target { get; }
    string Key { get; }

    /// <summary>Advance by the elapsed time. false — the animation has finished.</summary>
    bool Advance(TimeSpan elapsed);

    /// <summary>The animation was removed before it finished: displaced by a new one
    /// with the same key, or its target was removed from the tree. The implementation
    /// must bring its state to the finished one — whoever started it will not learn
    /// about the removal.</summary>
    /// <param name="applyFinalValue">Bring the value to the final one.
    /// false — leave it as is: the target is already leaving and there is nothing left to draw.</param>
    void Cancel(bool applyFinalValue);
}