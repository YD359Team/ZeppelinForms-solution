namespace ZeppelinForms.Android;

/// <summary>
/// Frames come from Choreographer. The interval is kept by skipping frames rather
/// than by a timer: the system won't wake us more often than the display rate
/// anyway, and a Handler of our own next to Choreographer would give jerky animation.
/// The arrangement is the same as BrowserFrameDriver's — neither platform owns
/// the loop, and there is no reason for them to diverge.
/// </summary>
internal sealed class AndroidFrameDriver(Action scheduleFrame, Action repaint) : IFrameDriver
{
    private int _intervalMs;
    private double _lastFrameMs;

    public bool IsRunning { get; private set; }

    public void Start(int intervalMs)
    {
        if (IsRunning) return;

        _intervalMs = intervalMs;

        // 0 means "there has been no tick yet": the first frame after Start is given
        // out right away, otherwise the animation would start with a one-interval delay
        _lastFrameMs = 0;
        IsRunning = true;

        scheduleFrame();
    }

    public void Stop() => IsRunning = false;

    public void RequestFrame() => repaint();

    internal bool ShouldTick(double timestampMs)
    {
        if (!IsRunning) return false;

        scheduleFrame();

        // the interval is a ceiling on the rate, not a strict measure: vsync comes
        // with jitter, and a tight comparison dropped every second frame, turning
        // sixty frames into thirty. A quarter of the interval covers the jitter,
        // while an extra frame on a 120 Hz display is still skipped
        if (_lastFrameMs != 0 && timestampMs - _lastFrameMs < _intervalMs * 0.75)
            return false;

        _lastFrameMs = timestampMs;
        return true;
    }
}