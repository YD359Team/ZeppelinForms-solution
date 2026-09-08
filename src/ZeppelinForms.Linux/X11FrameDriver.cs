namespace ZeppelinForms.Linux;

/// <summary>Кадры выдаёт цикл платформы: X11 таймеров окна не имеет,
/// поэтому платформа сама отмеряет интервал между итерациями.</summary>
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