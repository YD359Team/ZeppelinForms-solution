namespace ZeppelinForms;

/// <summary>Who asks for redraws. On desktop platforms it's a timer inside
/// the window, in the browser — requestAnimationFrame, on Android — Choreographer.</summary>
public interface IFrameDriver
{
    /// <summary>Whether continuous frame delivery is running.</summary>
    bool IsRunning { get; }

    /// <summary>Start delivering frames at the given interval.
    /// A repeated call while delivery is running changes nothing.</summary>
    void Start(int intervalMs);

    void Stop();

    /// <summary>One frame on demand, without continuous delivery.
    /// Useful where a single recompute without animation is needed.</summary>
    void RequestFrame();
}