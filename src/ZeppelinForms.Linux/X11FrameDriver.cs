namespace ZeppelinForms.Linux;

/// <summary>Frames are given out by the platform's loop: X11 windows have no
/// timers, so the platform measures the interval between iterations itself.</summary>
internal sealed class X11FrameDriver(X11Platform platform, X11Window window) : IFrameDriver
{
    public bool IsRunning { get; private set; }

    public void Start(int intervalMs)
    {
        if (IsRunning) return;

        platform.StartTicking(window, intervalMs);
        IsRunning = true;
    }

    public void Stop()
    {
        if (!IsRunning) return;

        platform.StopTicking(window);
        IsRunning = false;
    }

    public void RequestFrame() => window.Invalidate(null);
}