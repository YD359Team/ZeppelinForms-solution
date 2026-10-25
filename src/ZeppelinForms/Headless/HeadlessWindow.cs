using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Headless;

public sealed class HeadlessWindow : IPlatformWindow, IDesktopWindow, ITextInputMethodWindow
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

    /// <summary>The picture cursor asked for last; null after a cursor of a kind.</summary>
    public CursorImage? CursorImage { get; private set; }

    public void SetCursor(CursorKind cursor)
    {
        Cursor = cursor;
        CursorImage = null;
    }

    public void SetCursor(CursorImage cursor)
    {
        CursorImage = cursor;
        Cursor = cursor.Fallback;
    }

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

    /// <summary>The title bar the form asked for last.</summary>
    public bool TitleBarIsDark { get; private set; }

    /// <summary>The title bar's own color the form asked for last; null — none.</summary>
    public Color? TitleBarBackground { get; private set; }

    public void SetTitleBarAppearance(bool isDark, Color? background, Color? foreground)
    {
        TitleBarIsDark = isDark;
        TitleBarBackground = background;
    }

    internal HeadlessWindow(HeadlessPlatform platform, Form form)
    {
        _platform = platform;
        _form = form;

        // a platform reads these from the form when it makes the window, and is told
        // of their changes afterwards
        MinimumSizeLimit = form.MinimumSize;
        MaximumSizeLimit = form.MaximumSize;
        IsTopMost = form.TopMost;
    }

    public void Show()
    {
        IsShown = true;
        IsVisible = true;
        _form.PerformLayout();
    }

    /// <summary>The window is on the "screen": shown and not hidden. Checked in tests.</summary>
    public bool IsVisible { get; private set; }

    /// <summary>How many times the form hid its window — checked in tests.</summary>
    public int HideCount { get; private set; }

    public void SetVisible(bool visible)
    {
        if (!visible) HideCount++;

        IsVisible = visible;
    }

    public void Close()
    {
        if (IsClosed) return;

        IsClosed = true;
        IsShown = false;
        IsVisible = false;
        _platform.Remove(this);

        _form.OnWindowClosed();
    }

    public void SetTitle(string? title) => Title = title;

    /// <summary>Where the input method's windows would open: the focused field's caret,
    /// or null with no field focused. Checked in tests.</summary>
    public Rectangle? TextInputArea { get; private set; }

    public void SetTextInputArea(Rectangle? caret) => TextInputArea = caret;

    public void SetBounds(Rectangle bounds)
    {
        // a headless window has no frame and no screen: the size is the client area,
        // kept within the form's limits as a real window manager would keep it, and
        // both are reported back as a platform reports a move and a resize
        Size size = _form.ClampToSizeLimits(bounds.Size);

        _form.OnWindowMoved(new Point(bounds.X, bounds.Y));
        _form.OnWindowResized(size);

        _form.ClientSize = size;
        _form.PerformLayout();
    }

    public void Invalidate(Rectangle? rect = null)
    {
        InvalidateCount++;
        LastInvalidatedRect = rect;
    }

    public void Invoke(Action action) => _platform.Post(action);

    public void SetOpacity(float opacity) => Opacity = opacity;

    public void SetWindowState(WindowState state) => WindowState = state;


    /// <summary>How many times the frame was rebuilt — checked in tests: an open
    /// window follows FormBorderStyle, ControlBox and the rest at once.</summary>
    public int ChromeUpdateCount { get; private set; }

    public void UpdateChrome() => ChromeUpdateCount++;

    /// <summary>The size limits the form gave the window last — checked in tests.</summary>
    public Size MinimumSizeLimit { get; private set; }

    /// <summary>See <see cref="MinimumSizeLimit"/>.</summary>
    public Size MaximumSizeLimit { get; private set; }

    public void SetSizeLimits(Size minimum, Size maximum)
    {
        MinimumSizeLimit = minimum;
        MaximumSizeLimit = maximum;
    }

    /// <summary>Whether the form asked to be kept above the others.</summary>
    public bool IsTopMost { get; private set; }

    public void SetTopMost(bool topMost) => IsTopMost = topMost;

    /// <summary>How many times the form asked to be centered on its screen.</summary>
    public int CenterOnScreenCount { get; private set; }

    public void CenterOnScreen() => CenterOnScreenCount++;


    /// <summary>Whether the window lets itself be placed. False plays a Wayland window:
    /// for tests of what refuses a position there. True by default.</summary>
    public bool SupportsPositioning { get; set; } = true;

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