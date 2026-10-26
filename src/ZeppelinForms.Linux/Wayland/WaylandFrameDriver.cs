namespace ZeppelinForms.Linux.Wayland;

/// <summary>Frames are given out by the platform's loop, as on X11: the interval is
/// measured between its turns. The compositor's frame callbacks pace the painting, not
/// the ticks.</summary>
internal sealed class WaylandFrameDriver(WaylandPlatform platform, WaylandWindow window) : IFrameDriver
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