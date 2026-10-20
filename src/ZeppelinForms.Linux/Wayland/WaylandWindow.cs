using System.Reflection;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Linux.Wayland;

/// <summary>
/// A form's window on Wayland: a wl_surface with the xdg_toplevel role, drawn into
/// shared memory.
/// </summary>
/// <remarks>
/// <para>
/// The compositor leads: it sends a configure with the size and the states it wants, the
/// window acknowledges it and commits a frame of that size. Nothing is shown before the
/// first configure — a surface without a buffer is no window yet.
/// </para>
/// <para>
/// Frames are paced by the compositor's frame callbacks: one frame is in flight at a time,
/// and a window the compositor doesn't show — covered, on another workspace — draws
/// nothing until it is shown again. A configure is answered with a frame at once.
/// </para>
/// <para>
/// Sizes are logical — Wayland's surface coordinates are —, and the buffer is the size
/// times the integer buffer scale of the screens the window is on.
/// </para>
/// </remarks>
internal sealed class WaylandWindow : IPlatformWindow, IDesktopWindow
{
    private readonly WaylandPlatform _platform;
    private readonly Form _form;

    private WlSurface? _surface;
    private XdgSurface? _xdgSurface;
    private XdgToplevel? _toplevel;
    private WaylandShmSurface? _buffers;
    private WlCallback? _frameCallback;

    /// <summary>The screens the surface is on — for the scale, where the compositor
    /// doesn't say it itself (wl_surface before version 6).</summary>
    private readonly List<WaylandOutput> _enteredOutputs = [];

    /// <summary>The first configure came: the window may be drawn.</summary>
    private bool _configured;

    /// <summary>A configure was acknowledged and no frame answered it yet.</summary>
    private bool _configureUnanswered;

    // what the toplevel's configure asked for, applied by the xdg_surface's one
    private int _pendingWidth;
    private int _pendingHeight;
    private bool _pendingMaximized;
    private bool _pendingActivated;

    /// <summary>The client area in logical units, as Form.Size is on Linux.</summary>
    private int _width;
    private int _height;

    /// <summary>The size the window had last while not maximized: what it goes back to
    /// when the compositor leaves the size to it — a configure of 0 × 0 after the
    /// maximization is undone.</summary>
    private int _floatingWidth;
    private int _floatingHeight;

    private int _scale = 1;

    public WaylandWindow(WaylandPlatform platform, Form form)
    {
        _platform = platform;
        _form = form;
        Frames = new WaylandFrameDriver(platform, this);
    }

    public IFrameDriver Frames { get; }

    public float Scale => _scale;

    internal Form Form => _form;

    internal bool IsAlive => _surface is not null;

    internal bool IsInputEnabled { get; private set; } = true;

    /// <summary>The compositor composites ARGB buffers with their alpha.</summary>
    public bool SupportsTransparency => true;

    /// <summary>The compositor alone places windows.</summary>
    public bool SupportsPositioning => false;

    public void Create()
    {
        if (_surface is not null) return;

        // the place is the compositor's: a window asked to open at a point can't
        if (_form.WindowStartupLocation == WindowStartupLocation.Manual)
            throw new PlatformNotSupportedException(
                "A Wayland window can't open at a position of its own: the compositor places it. " +
                "Use WindowStartupLocation.Default, CenterScreen or CenterOwner, or check " +
                "App.Capabilities for WindowPositioning.");

        // the largest screen's scale until the compositor says which one the window is on:
        // the first frame is sharp on a HiDPI screen, and at worst downscaled on the others
        _scale = _platform.LargestOutputScale;

        _surface = _platform.Compositor.CreateSurface();
        _surface.Enter += OnSurfaceEnter;
        _surface.Leave += OnSurfaceLeave;
        _surface.PreferredBufferScale += scale => SetScale(Math.Max(1, scale));

        _xdgSurface = _platform.WmBase.GetXdgSurface(_surface);
        _xdgSurface.Configure += OnSurfaceConfigure;

        _toplevel = _xdgSurface.GetToplevel();
        _toplevel.Configure += OnToplevelConfigure;
        _toplevel.Close += OnCloseRequested;

        _toplevel.SetTitle(_form.Title ?? string.Empty);
        _toplevel.SetAppId(AppId);

        // an owned form — a dialog — is the owner's child: the compositor keeps it above
        // the owner and, as a rule, puts it over the owner's middle. This is all of
        // CenterOwner Wayland has
        if (_form.Owner?.PlatformWindow is WaylandWindow { _toplevel: { } parent })
            _toplevel.SetParent(parent);

        ApplySizeLimits(_form.MinimumSize, _form.MaximumSize);

        _buffers = new WaylandShmSurface(_platform.Shm, _platform.Graphics);

        // the size the form asks for, until the compositor sends one of its own
        Size opening = _form.ClampToSizeLimits(_form.Size);
        ApplySize(Math.Max(1, (int)MathF.Round(opening.Width)), Math.Max(1, (int)MathF.Round(opening.Height)));

        _platform.Register(this);
    }

    /// <summary>The application's id for the compositor: what it groups windows by and
    /// finds the .desktop file with.</summary>
    private static string AppId =>
        Assembly.GetEntryAssembly()?.GetName().Name ?? "ZeppelinForms";

    public void Show()
    {
        Create();

        // a state asked before the window is shown goes with its first commit
        if (_form.WindowState == WindowState.Maximized)
            _toplevel!.SetMaximized();

        // the first commit has no buffer: it asks the compositor for a configure, and the
        // window appears with the frame that answers it
        _surface!.Commit();
    }

    public void Close()
    {
        if (_surface is null) return;

        // a frame callback of a destroyed surface may never be done: forgotten, or it
        // would keep this window alive through its handler
        _frameCallback?.Abandon();
        _frameCallback = null;

        // the role before the surface, the surface before its buffers: a buffer the
        // compositor still shows is let go of with the surface
        _toplevel?.Destroy();
        _xdgSurface?.Destroy();
        _surface.Destroy();
        _buffers?.Dispose();

        _toplevel = null;
        _xdgSurface = null;
        _surface = null;
        _buffers = null;

        _platform.StopTicking(this);
        _platform.Unregister(this);

        _form.OnWindowClosed();
    }

    private void OnCloseRequested()
    {
        // the close button of the compositor's or the desktop's: the form's Closing may
        // keep the window open
        if (_form.RequestClose(CloseReason.UserClosing))
            Close();
    }

    private void OnToplevelConfigure(int width, int height, byte[] states)
    {
        _pendingWidth = width;
        _pendingHeight = height;
        _pendingMaximized = false;
        _pendingActivated = false;

        // an array of uint32 xdg_toplevel.state values
        for (int i = 0; i + 4 <= states.Length; i += 4)
        {
            switch ((XdgToplevel.State)BitConverter.ToUInt32(states, i))
            {
                case XdgToplevel.State.Maximized:
                case XdgToplevel.State.Fullscreen:
                    _pendingMaximized = true;
                    break;

                case XdgToplevel.State.Activated:
                    _pendingActivated = true;
                    break;
            }
        }
    }

    /// <summary>The end of a configure sequence: what the toplevel's configure asked for
    /// is taken, acknowledged, and answered with a frame.</summary>
    private void OnSurfaceConfigure(uint serial)
    {
        _xdgSurface!.AckConfigure(serial);

        _configured = true;
        _configureUnanswered = true;

        if (_pendingWidth > 0 && _pendingHeight > 0)
        {
            Size size = _pendingMaximized
                ? new Size(_pendingWidth, _pendingHeight)       // a maximized window fills what it is given
                : _form.ClampToSizeLimits(new Size(_pendingWidth, _pendingHeight));

            ApplySize((int)size.Width, (int)size.Height);
        }
        else if (!_pendingMaximized && _floatingWidth > 0)
        {
            // 0 — the size is the client's to choose: the one it had before it was
            // maximized, or the one it has
            ApplySize(_floatingWidth, _floatingHeight);
        }

        // xdg-shell never says a window is minimized: one the application minimized
        // stays so until the compositor shows it active again
        WindowState state = _pendingMaximized ? WindowState.Maximized
            : _form.WindowState == WindowState.Minimized && !_pendingActivated ? WindowState.Minimized
            : WindowState.Normal;

        _form.SetWindowStateFromPlatform(state);

        // the keyboard's state goes with the activation, as with X11's FocusOut
        if (!_pendingActivated && _form.IsActive)
            _form.OnWindowFocusLost();

        _form.OnWindowActivated(_pendingActivated);

        Invalidate(null);
    }

    /// <summary>The client area is this size in logical units: the form's client area and
    /// layout follow, and a frame is asked for.</summary>
    private void ApplySize(int width, int height)
    {
        if (width == _width && height == _height) return;

        _width = width;
        _height = height;

        if (!_pendingMaximized)
        {
            _floatingWidth = width;
            _floatingHeight = height;
        }

        _form.ClientSize = new Size(width, height);

        // Form.Size is the client area on Linux: it follows the window as well
        _form.OnWindowResized(_form.ClientSize);
        _form.PerformLayout();

        Invalidate(null);
    }

    private void OnSurfaceEnter(WlOutput? proxy)
    {
        if (_platform.FindOutput(proxy) is { } output && !_enteredOutputs.Contains(output))
            _enteredOutputs.Add(output);

        UpdateScaleFromOutputs();
    }

    private void OnSurfaceLeave(WlOutput? proxy)
    {
        if (_platform.FindOutput(proxy) is { } output)
            _enteredOutputs.Remove(output);

        UpdateScaleFromOutputs();
    }

    /// <summary>Before wl_surface 6 the scale is the client's guess: the largest of the
    /// screens the window is on. From 6 the compositor names it, and that is taken.</summary>
    private void UpdateScaleFromOutputs()
    {
        if (_surface is null || _surface.Version >= 6 || _enteredOutputs.Count == 0) return;

        SetScale(_enteredOutputs.Max(o => o.Scale));
    }

    private void SetScale(int scale)
    {
        if (scale == _scale) return;

        // the layout is logical and stays; the buffers are made anew at the next frame
        _scale = scale;
        Invalidate(null);
    }

    // a frame has been requested and not drawn yet. Every frame is whole: the two
    // buffers take turns, and the one drawn into holds a frame older than the last
    private bool _paintRequested;

    public void Invalidate(Rectangle? rect) => _paintRequested = true;

    /// <summary>A frame is asked for and may go now: the window is configured, the last
    /// frame was shown — or a configure waits for its answer — and a buffer is free.</summary>
    internal bool CanPaintNow =>
        _paintRequested
        && _configured
        && (_frameCallback is null || _configureUnanswered)
        && _buffers is { HasFreeBuffer: true };

    internal void PaintIfRequested()
    {
        if (!CanPaintNow) return;

        // reset before drawing: drawing may ask for the next frame — an animation does
        _paintRequested = false;

        int width = _width * _scale;
        int height = _height * _scale;

        if (_buffers!.BeginFrame(width, height) is not { } g)
        {
            _paintRequested = true;
            return;
        }

        FormRenderer.Render(_form, g, _scale);

        WlBuffer buffer = _buffers.EndFrame();

        _form.TakeDirtyRegion();

        WlSurface surface = _surface!;

        // buffer_scale from version 3, damage in buffer pixels from 4: the compositor
        // is bound at 6 where it can, and older ones are still spoken to
        if (surface.Version >= 3) surface.SetBufferScale(_scale);

        surface.Attach(buffer, 0, 0);

        if (surface.Version >= 4) surface.DamageBuffer(0, 0, width, height);
        else surface.Damage(0, 0, _width, _height);

        // the next frame when the compositor has shown this one
        _frameCallback?.Abandon();
        _frameCallback = surface.Frame();
        _frameCallback.Done += _ => _frameCallback = null;

        surface.Commit();

        _configureUnanswered = false;
    }

    internal void RaiseTick() => _form.Tick();

    public void Invoke(Action action) => _platform.Invoke(action);

    public void SetTitle(string? title) => _toplevel?.SetTitle(title ?? string.Empty);

    /// <summary>The size of <paramref name="bounds"/>: a client of Wayland chooses its own
    /// size. The position is refused by Form.Position before it comes here, and a size
    /// set from code keeps the place the compositor gave.</summary>
    public void SetBounds(Rectangle bounds)
    {
        if (_surface is null) return;

        Size size = _form.ClampToSizeLimits(bounds.Size);

        ApplySize(Math.Max(1, (int)MathF.Round(size.Width)), Math.Max(1, (int)MathF.Round(size.Height)));
    }

    public void SetSizeLimits(Size minimum, Size maximum)
    {
        if (_toplevel is null) return;

        ApplySizeLimits(minimum, maximum);

        // a window already out of the new limits is brought inside; the limits
        // themselves take effect with the next commit — the frame this asks for
        Size inside = _form.ClampToSizeLimits(new Size(_width, _height));
        ApplySize((int)inside.Width, (int)inside.Height);

        Invalidate(null);
    }

    private void ApplySizeLimits(Size minimum, Size maximum)
    {
        // 0 — no limit, as NaN is for the form
        _toplevel!.SetMinSize(Limit(minimum.Width), Limit(minimum.Height));
        _toplevel.SetMaxSize(Limit(maximum.Width), Limit(maximum.Height));

        static int Limit(float value) =>
            float.IsNaN(value) || float.IsInfinity(value) || value <= 0 ? 0 : (int)MathF.Ceiling(value);
    }

    public void SetWindowState(WindowState state)
    {
        if (_toplevel is null) return;

        switch (state)
        {
            case WindowState.Maximized:
                _toplevel.SetMaximized();
                break;

            case WindowState.Minimized:
                // the compositor may refuse, and says nothing either way
                _toplevel.SetMinimized();
                break;

            case WindowState.Normal:
                // a minimized window can't be brought back by its client on Wayland:
                // only the maximization is undone
                _toplevel.UnsetMaximized();
                break;
        }
    }

    /// <summary>The compositor alone places windows: there is no screen to center on
    /// that the window could ask for.</summary>
    public void CenterOnScreen() =>
        throw new PlatformNotSupportedException(
            "A Wayland window can't be centered by the application: the compositor places it. " +
            "Check App.Capabilities for WindowPositioning first.");

    /// <summary>Wayland has no window opacity of its own; a translucent form draws its
    /// background with alpha instead.</summary>
    public void SetOpacity(float opacity)
    {
    }

    /// <summary>Wayland lets no client keep itself above the others.</summary>
    public void SetTopMost(bool topMost)
    {
    }

    public void SetEnabled(bool enabled) => IsInputEnabled = enabled;

    // pointer and keyboard input, cursors, activation, the clipboard and drag and
    // drop are not here yet: until they come the window only shows the form
    public void Activate()
    {
    }

    public void CaptureMouse()
    {
    }

    public void ReleaseMouseCapture()
    {
    }

    public void SetCursor(CursorKind cursor)
    {
    }

    public void SetDragDropEnabled(bool enabled)
    {
    }
}