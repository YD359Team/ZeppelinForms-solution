using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Headless;

public sealed class HeadlessWindow : IPlatformWindow, IDesktopWindow
{
    private readonly HeadlessPlatform _platform;
    private readonly Form _form;

    public void SetDragDropEnabled(bool enabled) { }

    public bool IsShown { get; private set; }
    public bool IsClosed { get; private set; }
    public string? Title { get; private set; }
    public float Opacity { get; private set; } = 1f;
    public WindowState WindowState { get; private set; }

    public CursorKind Cursor { get; private set; } = CursorKind.Default;
    public void SetCursor(CursorKind cursor) => Cursor = cursor;

    /// <summary>How many times a redraw was requested — checked in tests.</summary>
    public int InvalidateCount { get; private set; }

    public Rectangle? LastInvalidatedRect { get; private set; }

    public bool SupportsTransparency => true;

    public float Scale => 1f;

    /// <summary>Checked in modality tests: a deafened owner is exactly
    /// what modality is on platforms without a nested loop.</summary>
    public bool IsEnabled { get; private set; } = true;
    public bool IsActive { get; private set; }

    public void SetEnabled(bool enabled) => IsEnabled = enabled;
    public void Activate() => IsActive = true;

    internal HeadlessWindow(HeadlessPlatform platform, Form form)
    {
        _platform = platform;
        _form = form;
    }

    public void Show()
    {
        IsShown = true;
        _form.PerformLayout();
    }

    public void Close()
    {
        if (IsClosed) return;

        IsClosed = true;
        IsShown = false;
        _platform.Remove(this);

        _form.OnWindowClosed();
    }

    public void SetTitle(string? title) => Title = title;

    public void SetBounds(Rectangle bounds)
    {
        _form.ClientSize = bounds.Size;
        _form.PerformLayout();
    }

    public void Invalidate(Rectangle? bounds = null)
    {
        InvalidateCount++;
        LastInvalidatedRect = bounds;
    }

    public void Invoke(Action action) => _platform.Post(action);

    public void SetOpacity(float opacity) => Opacity = opacity;

    public void SetWindowState(WindowState state) => WindowState = state;


    /// <summary>How many times the frame was rebuilt — checked in tests: an open
    /// window follows FormBorderStyle, ControlBox and the rest at once.</summary>
    public int ChromeUpdateCount { get; private set; }

    public void UpdateChrome() => ChromeUpdateCount++;

    /// <summary>Frames don't run by themselves in headless: nothing ticks
    /// unless a test calls Tick. The step of such a frame is still taken from
    /// the stopwatch, as on any platform; for an exact step tests use
    /// FrameClock.Advance.</summary>
    private sealed class NoFrames : IFrameDriver
    {
        public bool IsRunning { get; private set; }

        public void Start(int intervalMs) => IsRunning = true;
        public void Stop() => IsRunning = false;
        public void RequestFrame() { }
    }

    public IFrameDriver Frames { get; } = new NoFrames();

    /// <summary>Deliver one frame, as the platform timer would. Animations advance
    /// by the real time since the previous frame, capped like on any platform.</summary>
    public void Tick() => _form.Tick();

    /// <summary>Set the client area size and recompute the layout.</summary>
    public void Resize(float width, float height)
    {
        _form.ClientSize = new Size(width, height);
        _form.PerformLayout();
    }

    public void CaptureMouse() { }

    public void ReleaseMouseCapture() { }
}