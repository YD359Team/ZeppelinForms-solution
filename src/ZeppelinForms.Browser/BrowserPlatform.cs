using SkiaSharp;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;

namespace ZeppelinForms.Browser;

/// <summary>
/// The browser platform. There is one canvas for the whole application, so windows
/// here are layers on a shared surface rather than separate system windows: the
/// surface, the Invoke queue and input distribution live in the platform, the
/// windows only know their place on it.
///
/// It doesn't implement INestedLoopSupport: blocking the thread and keeping on
/// receiving events is impossible in a browser, so Form.ShowDialog honestly
/// throws — only ShowDialogAsync works.
/// </summary>
public sealed class BrowserPlatform : IPlatform, IAppLifecycle, ISystemMotionSettings
{
    /// <summary>The dimming under a modal dialog. A browser has no windows of its
    /// own, and without it it's unclear that the form below stopped taking input.</summary>
    private static readonly SKColor s_scrim = new(0, 0, 0, 96);

    public event EventHandler? Paused;
    public event EventHandler? Resumed;
    public event EventHandler? Saving;

    private bool _reducedMotion;

    public bool PrefersReducedMotion => _reducedMotion;

    /// <summary>The setting changed on the fly — the media query reports it.</summary>
    event EventHandler? ISystemMotionSettings.Changed
    {
        add => _motionChanged += value;
        remove => _motionChanged -= value;
    }

    private EventHandler? _motionChanged;

    internal void HandleReducedMotionChange(bool reduced)
    {
        if (reduced == _reducedMotion) return;

        _reducedMotion = reduced;
        _motionChanged?.Invoke(this, EventArgs.Empty);
    }

    internal static BrowserPlatform? Current { get; private set; }

    private readonly string _canvasId;
    private readonly BrowserSkiaSurface _surface = new();
    private readonly List<BrowserWindow> _windows = [];
    private readonly Queue<Action> _invokeQueue = new();

    private int _physicalWidth;
    private int _physicalHeight;
    private bool _initialized;
    private bool _paintPending;

    private BrowserPlatform(string canvasId)
    {
        _canvasId = canvasId;
    }

    /// <summary>
    /// The zf module registers the page loader through setModuleImports —
    /// this way the path to it stays in main.js rather than depending on where
    /// the SDK decides to put additional files.
    /// </summary>
    public static BrowserPlatform Create(string canvasId = "zf-canvas")
    {
        Skia.SkiaImageDecoder.Register();
        Skia.SkiaTextMeasurer.Register();
        Skia.SkiaOffscreenRenderer.Register();
        BrowserClipboard.Register();
        BrowserFilePicker.Register();
        Displays.Current = new BrowserDisplayProvider();

        var platform = new BrowserPlatform(canvasId);
        Current = platform;

        platform._reducedMotion = Interop.PrefersReducedMotion();
        ZeppelinForms.Animation.Motion.UseSystemSettings(platform);

        return platform;
    }

    /// <summary>
    /// Fetch files from the server and put them into the virtual FS at the same paths.
    /// Needed by everything that reads from disk synchronously — Image.LoadAsset,
    /// Font.WithFile — because in a browser nothing can be downloaded on the go:
    /// fetch is asynchronous, and those calls can't wait.
    ///
    /// The paths are absolute and match the addresses on the server: "/Assets/x.png"
    /// is downloaded from wwwroot/Assets/x.png and lands at the same place in the FS.
    /// </summary>
    public static async Task PreloadAsync(params string[] paths)
    {
        using HttpClient http = new() { BaseAddress = new Uri(Interop.BaseUri()) };

        foreach (string path in paths)
        {
            // a leading slash would take the request to the site root, past the base address
            using HttpResponseMessage response = await http.GetAsync(path.TrimStart('/'));

            response.EnsureSuccessStatusCode();

            // a single-page application server answers an unknown address not with 404
            // but with its index.html and status 200. Without this check the HTML goes
            // into the file system under the name of a picture or a font, and the error
            // surfaces much later — in the decoder
            if (response.Content.Headers.ContentType?.MediaType is "text/html")
            {
                throw new InvalidDataException(
                    $"The server returned HTML instead of a file at {path}. " +
                    "Most likely the resource isn't published, and the request was " +
                    "taken over by the application page fallback.");
            }

            byte[] bytes = await response.Content.ReadAsByteArrayAsync();

            if (Path.GetDirectoryName(path) is { Length: > 0 } directory)
                Directory.CreateDirectory(directory);

            File.WriteAllBytes(path, bytes);
        }
    }

    internal float Scale { get; private set; } = 1f;

    /// <summary>The canvas size in logical units.</summary>
    internal Size CanvasSize => new(_physicalWidth / Scale, _physicalHeight / Scale);

    /// <summary>The bottom form takes the whole canvas; everything above it is dialogs.</summary>
    private BrowserWindow? Root => _windows.Count > 0 ? _windows[0] : null;

    public IPlatformWindow CreateWindow(Form form)
    {
        var window = new BrowserWindow(this, form);

        _windows.Add(window);
        form.PlatformWindow = window;
        form.Platform = this;

        if (!_initialized)
        {
            _initialized = true;
            Interop.Platform = this;

            // init calls resize itself, and that calls HandleResize: the canvas size
            // is unknown until then, there is nothing to lay out
            Interop.Init(_canvasId);
            Interop.SetTitle(form.Title ?? string.Empty);
            if (form.Icon is { } icon)
            {
                // browsers accept ICO, and a data address spares a separate file
                // in wwwroot — the icon keeps a single source
                string base64 = Convert.ToBase64String(icon.GetRawData());
                Interop.SetFavicon($"data:image/x-icon;base64,{base64}");
            }
        }
        else
        {
            LayoutOverlay(window);

            // deferred, like every other repaint here: a dialog's window is created
            // in the middle of BeginDialog, before it is shown and before its owner
            // is dimmed — painting right away drew that half-made state
            Invalidate();
        }

        return window;
    }

    internal void BringToFront(BrowserWindow window)
    {
        // the bottom form is not raised: it is stretched over the canvas,
        // and the dialogs under it would become invisible
        if (_windows.Count < 2 || _windows[0] == window) return;
        if (_windows[^1] == window) return;

        _windows.Remove(window);
        _windows.Add(window);

        Invalidate();
    }

    internal void Remove(BrowserWindow window)
    {
        if (!_windows.Remove(window)) return;

        Invalidate();
    }

    /// <summary>The browser owns the loop, so this returns control immediately.
    /// Everything else happens in event handlers and rAF.</summary>
    public void Start() { }

    public void Exit()
    {
        // from the end: closing removes the window from the list
        for (int i = _windows.Count - 1; i >= 0; i--)
            _windows[i].Close();
    }

    // ==== the surface ====

    internal void HandleResize(int physicalWidth, int physicalHeight, float scale)
    {
        Scale = scale <= 0 ? 1f : scale;
        _physicalWidth = physicalWidth;
        _physicalHeight = physicalHeight;

        _surface.Resize(physicalWidth, physicalHeight);

        if (Root is { } root)
        {
            root.Form.ClientSize = CanvasSize;
            root.Form.PerformLayout();
        }

        // dialogs are tied to the canvas center, and it has just moved
        for (int i = 1; i < _windows.Count; i++)
            LayoutOverlay(_windows[i]);

        // synchronous on purpose, unlike everything else: resizing a canvas clears
        // it, and waiting for the next frame would flash an empty canvas
        Paint();
    }

    /// <summary>A dialog keeps its own size and stands in the center.
    /// If it doesn't fit, it shrinks to the canvas: it has nowhere else to go,
    /// nothing beyond the canvas edges is visible.</summary>
    private void LayoutOverlay(BrowserWindow window)
    {
        Size canvas = CanvasSize;
        Size requested = window.Form.Size;

        float width = requested.IsWidthAuto || requested.Width <= 0
            ? canvas.Width * 0.6f
            : Math.Min(requested.Width, canvas.Width);

        float height = requested.IsHeightAuto || requested.Height <= 0
            ? canvas.Height * 0.4f
            : Math.Min(requested.Height, canvas.Height);

        window.Form.ClientSize = new Size(width, height);
        window.Origin = new Point((canvas.Width - width) / 2f, (canvas.Height - height) / 2f);

        window.Form.PerformLayout();
    }

    /// <summary>Mark the canvas stale. Drawing right here is not allowed:
    /// one user action brings dozens of Invalidates — from layout, from an image
    /// loading, from every control — and a browser frame is a full one, copying
    /// the whole buffer into ImageData. So they are accumulated and drawn once in rAF.</summary>
    internal void Invalidate()
    {
        if (_paintPending) return;

        _paintPending = true;
        Interop.RequestFrame();
    }

    private void Paint()
    {
        if (_surface.BeginFrame() is not SKSurface surface) return;

        SKCanvas canvas = surface.Canvas;

        for (int i = 0; i < _windows.Count; i++)
        {
            BrowserWindow window = _windows[i];
            bool isRoot = i == 0;

            if (!isRoot)
                DimBelow(canvas);

            Skia.SkiaRenderer.Render(
                window.Form,
                canvas,
                Scale,
                clip: null,
                clearBackground: isRoot,
                origin: window.Origin);

            window.Form.TakeDirtyRegion();
        }

        _surface.EndFrame();
    }

    private void DimBelow(SKCanvas canvas)
    {
        canvas.Save();
        canvas.Scale(Scale, Scale);

        using var paint = new SKPaint { Color = s_scrim };
        canvas.DrawRect(new SKRect(0, 0, CanvasSize.Width, CanvasSize.Height), paint);

        canvas.Restore();
    }

    // ==== frames and the queue ====

    internal void HandleFrame(double timestampMs)
    {
        // a copy: a tick may open or close a window
        foreach (BrowserWindow window in _windows.ToArray())
            window.HandleFrame(timestampMs);

        // an animation tick marks the canvas stale — it is checked after it
        if (!_paintPending) return;

        _paintPending = false;
        Paint();
    }

    internal void Enqueue(Action action)
    {
        _invokeQueue.Enqueue(action);
        Interop.ScheduleDrain();
    }

    internal void DrainInvokes()
    {
        // Count is fixed up front: an action may queue a new one, and without
        // this draining the queue might never end
        int pending = _invokeQueue.Count;

        for (int i = 0; i < pending; i++)
            _invokeQueue.Dequeue()();
    }

    // ==== input ====

    /// <summary>Input goes to the topmost window that accepts it. The dialog's owner
    /// is muted through SetEnabled meanwhile, so no separate modality check is needed.</summary>
    internal BrowserWindow? InputTarget()
    {
        for (int i = _windows.Count - 1; i >= 0; i--)
        {
            if (_windows[i].IsInputEnabled)
                return _windows[i];
        }

        return null;
    }

    internal void HandleVisibilityChange(bool visible)
    {
        if (visible)
            Resumed?.Invoke(this, EventArgs.Empty);
        else
            Paused?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>pagehide rather than beforeunload: on mobile browsers the latter often
    /// doesn't come at all, while the former is the last guaranteed point.</summary>
    internal void HandlePageHide() => Saving?.Invoke(this, EventArgs.Empty);
}