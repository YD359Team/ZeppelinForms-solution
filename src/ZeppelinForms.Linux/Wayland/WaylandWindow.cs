using System.Reflection;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Linux.Wayland;

/// <summary>
/// A form's window on Wayland: a wl_surface with the xdg_toplevel role, drawn on the GPU
/// through EGL or into shared memory.
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
/// <para>
/// The frame is the compositor's where it draws one — asked through xdg-decoration — and
/// the window's own otherwise, see <see cref="WaylandDecoration"/>: the surface is then
/// larger than the form, and the pointer's events are the frame's or the form's by where
/// they fall.
/// </para>
/// </remarks>
internal sealed class WaylandWindow : IPlatformWindow, IDesktopWindow, ITextInputMethodWindow
{
    private readonly WaylandPlatform _platform;
    private readonly Form _form;

    private WlSurface? _surface;
    private XdgSurface? _xdgSurface;
    private XdgToplevel? _toplevel;
    private IWaylandSurface? _buffers;
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

    // the frame: the compositor's when it agreed to draw one, the window's own otherwise
    private ZxdgToplevelDecorationV1? _serverDecoration;
    private bool _serverSide;
    private bool _pendingServerSide;
    private readonly WaylandDecoration _decoration = new();

    // the states of the last configure applied
    private bool _maximized;
    private bool _pendingTiled;
    private bool _tiled;

    // what the compositor lets a window ask for; all, until it says otherwise (xdg-shell 5)
    private bool _canMinimize = true;
    private bool _canMaximize = true;

    /// <summary>The window geometry last told the compositor.</summary>
    private Rectangle _geometry;

    // the pointer over the surface: where, and whose its press is — a press stays with
    // the form or the frame it began in until its buttons are up, as Wayland's grab does
    private enum PressTarget { None, Form, Frame }

    private Point _pointerAt;
    private PressTarget _pressTarget;
    private int _formButtonsDown;
    private bool _formHasPointer;
    private CursorKind? _frameCursor;

    // the caption's last click, for a double click that maximizes
    private long _lastCaptionClickMs;
    private Point _lastCaptionClickAt;

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

    /// <summary>The window's surface: what the seat's events name the window by.</summary>
    internal WlSurface? Surface => _surface;

    /// <summary>The integer scale the window's buffers are drawn at: the cursor's theme is
    /// loaded at it too.</summary>
    internal int BufferScale => _scale;

    /// <summary>The cursor the form set: a kind of the theme, or a picture.</summary>
    internal CursorKind CursorKind { get; private set; } = CursorKind.Default;

    internal CursorImage? CursorPicture { get; private set; }

    /// <summary>The cursor shown now: the frame's over the frame — the resize arrows —,
    /// the form's over the form.</summary>
    internal CursorKind EffectiveCursorKind => _frameCursor ?? CursorKind;

    internal CursorImage? EffectiveCursorPicture => _frameCursor is null ? CursorPicture : null;

    /// <summary>The window draws its own frame: there is one to draw, and the compositor
    /// draws none.</summary>
    private bool DrawsFrame => _form.FormBorderStyle != FormBorderStyle.None && !_serverSide;

    private WaylandDecoration? DrawnFrame => DrawsFrame ? _decoration : null;

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

        _toplevel.WmCapabilities += OnWmCapabilities;

        _toplevel.SetTitle(_form.Title ?? string.Empty);
        _toplevel.SetAppId(AppId);

        // the compositor is asked to draw the frame; its answer comes with the first
        // configure, and until then the window counts on drawing its own
        if (_platform.DecorationManager is { } manager && !WaylandPlatform.ClientDecorationsForced)
        {
            _serverDecoration = manager.GetToplevelDecoration(_toplevel);
            _serverDecoration.Configure += mode =>
                _pendingServerSide = mode == (uint)ZxdgToplevelDecorationV1.Mode.ServerSide;

            RequestDecorationMode();
        }

        SyncFrame();

        // an owned form — a dialog — is the owner's child: the compositor keeps it above
        // the owner and, as a rule, puts it over the owner's middle. This is all of
        // CenterOwner Wayland has
        if (_form.Owner?.PlatformWindow is WaylandWindow { _toplevel: { } parent })
            _toplevel.SetParent(parent);

        ApplySizeLimits(_form.MinimumSize, _form.MaximumSize);

        // on the GPU through EGL where it works, in shared memory otherwise
        _buffers = WaylandSurfaceFactory.Create(_platform, _surface, _platform.Graphics);

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

        // the decoration before its toplevel, as the protocol asks
        _serverDecoration?.Destroy();
        _serverDecoration = null;

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
        _pendingTiled = false;

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

                case XdgToplevel.State.TiledLeft:
                case XdgToplevel.State.TiledRight:
                case XdgToplevel.State.TiledTop:
                case XdgToplevel.State.TiledBottom:
                    _pendingTiled = true;
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

        // the frame first: whose it is and how it looks decide what of the size is the form's
        bool frameWasDrawn = DrawsFrame;

        _serverSide = _pendingServerSide;
        _maximized = _pendingMaximized;
        _tiled = _pendingTiled;
        _decoration.IsActive = _pendingActivated;

        SyncFrame();

        // the limits are the window's, title bar included: they move when the frame changes hands
        if (frameWasDrawn != DrawsFrame)
            ApplySizeLimits(_form.MinimumSize, _form.MaximumSize);

        if (_pendingWidth > 0 && _pendingHeight > 0)
        {
            // the size is the window geometry's: the title bar the window draws is in it
            float caption = DrawnFrame?.Caption ?? 0f;
            int height = Math.Max(1, _pendingHeight - (int)caption);

            Size size = _pendingMaximized
                ? new Size(_pendingWidth, height)       // a maximized window fills what it is given
                : _form.ClampToSizeLimits(new Size(_pendingWidth, height));

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
        && _buffers is { CanBeginFrame: true };

    internal void PaintIfRequested()
    {
        if (!CanPaintNow) return;

        // reset before drawing: drawing may ask for the next frame — an animation does
        _paintRequested = false;

        WaylandDecoration? frame = DrawnFrame;
        var client = new Size(_width, _height);

        // the surface is the form, or the form in the frame the window draws round it
        Size surfaceSize = frame?.SurfaceSize(client) ?? client;

        int width = (int)surfaceSize.Width * _scale;
        int height = (int)surfaceSize.Height * _scale;

        if (_buffers!.BeginFrame(width, height) is not { } g)
        {
            _paintRequested = true;
            return;
        }

        if (frame is not null)
        {
            // the band round the window is see-through but for its shadow
            g.Clear(Colors.Transparent);

            g.Save();
            g.Scale(_scale, _scale);
            frame.Draw(g, client);
            g.Restore();

            FormRenderer.Render(_form, g, _scale, clip: null, clearBackground: false, origin: frame.ContentOrigin);
        }
        else
        {
            FormRenderer.Render(_form, g, _scale);
        }

        _form.TakeDirtyRegion();

        WlSurface surface = _surface!;

        // where the window proper is on the surface: the compositor places, snaps and
        // maximizes by it, the band left out
        Rectangle geometry = frame?.Geometry(client) ?? new Rectangle(0, 0, _width, _height);

        if (geometry != _geometry)
        {
            _xdgSurface!.SetWindowGeometry((int)geometry.X, (int)geometry.Y, (int)geometry.Width, (int)geometry.Height);
            _geometry = geometry;
        }

        // buffer_scale from version 3: the compositor is bound at 6 where it can, and
        // older ones are still spoken to
        if (surface.Version >= 3) surface.SetBufferScale(_scale);

        // the next frame when the compositor has shown this one; asked before the
        // commit the frame ends in — the surface's own, or EGL's swap
        _frameCallback?.Abandon();
        _frameCallback = surface.Frame();
        _frameCallback.Done += _ => _frameCallback = null;

        _buffers.EndFrame(surface, width, height);

        _configureUnanswered = false;
    }

    internal void RaiseTick() => _form.Tick();

    public void Invoke(Action action) => _platform.Invoke(action);

    public void SetTitle(string? title)
    {
        _toplevel?.SetTitle(title ?? string.Empty);

        _decoration.Title = title ?? string.Empty;
        if (DrawsFrame) Invalidate(null);
    }

    /// <summary>The frame the window draws takes the form's title bar: dark or light, and
    /// its colors. The compositor's frame takes none of it — Wayland has no way to ask.</summary>
    public void SetTitleBarAppearance(bool isDark, Color? background, Color? foreground)
    {
        _decoration.IsDark = isDark;
        _decoration.Background = background;
        _decoration.Foreground = foreground;

        if (DrawsFrame) Invalidate(null);
    }

    /// <summary>FormBorderStyle, ControlBox, CanMinimize, CanMaximize or CanResize changed:
    /// the compositor is asked anew whose frame it is, and the window's own is rebuilt.</summary>
    public void UpdateChrome()
    {
        if (_toplevel is null) return;

        bool frameWasDrawn = DrawsFrame;

        RequestDecorationMode();
        SyncFrame();

        if (frameWasDrawn != DrawsFrame)
            ApplySizeLimits(_form.MinimumSize, _form.MaximumSize);

        Invalidate(null);
    }

    /// <summary>The compositor's frame for a window that has one; none — the window's own,
    /// which it then doesn't draw — for <see cref="FormBorderStyle.None"/>.</summary>
    private void RequestDecorationMode() =>
        _serverDecoration?.SetMode(_form.FormBorderStyle == FormBorderStyle.None
            ? (uint)ZxdgToplevelDecorationV1.Mode.ClientSide
            : (uint)ZxdgToplevelDecorationV1.Mode.ServerSide);

    /// <summary>The frame the window draws, from the form and the window's state.</summary>
    private void SyncFrame()
    {
        _decoration.Title = _form.Title ?? string.Empty;
        _decoration.IsMaximized = _maximized;
        _decoration.FillsPlace = _maximized || _tiled;
        _decoration.IsToolWindow = _form.IsToolWindow;
        _decoration.IsResizable = _form.IsResizable;
        _decoration.HasClose = _form.ControlBox;
        _decoration.HasMinimize = _form.HasMinimizeBox && _canMinimize;
        _decoration.HasMaximize = _form.HasMaximizeBox && _form.IsResizable && _canMaximize;
    }

    /// <summary>What the compositor lets a window ask for: a button it would ignore is not shown.</summary>
    private void OnWmCapabilities(byte[] capabilities)
    {
        _canMinimize = false;
        _canMaximize = false;

        for (int i = 0; i + 4 <= capabilities.Length; i += 4)
        {
            switch ((XdgToplevel.WmCapabilitiesKind)BitConverter.ToUInt32(capabilities, i))
            {
                case XdgToplevel.WmCapabilitiesKind.Minimize:
                    _canMinimize = true;
                    break;

                case XdgToplevel.WmCapabilitiesKind.Maximize:
                    _canMaximize = true;
                    break;
            }
        }
    }

    // ---- the pointer, in the surface's coordinates: the frame's or the form's ----

    /// <summary>A point of the surface in the form's coordinates: a drop's, as the pointer's.</summary>
    internal Point ToFormPoint(Point surfacePoint) => ToForm(surfacePoint);

    /// <summary>A point of the surface in the form's coordinates.</summary>
    private Point ToForm(Point surfacePoint) =>
        DrawnFrame is { } frame
            ? new Point(surfacePoint.X - frame.ContentOrigin.X, surfacePoint.Y - frame.ContentOrigin.Y)
            : surfacePoint;

    private DecorationPart PartAt(Point surfacePoint) =>
        DrawnFrame?.HitTest(surfacePoint, new Size(_width, _height)) ?? DecorationPart.Client;

    internal void OnPointerMotion(Point at, KeyModifiers modifiers)
    {
        _pointerAt = at;
        DecorationPart part = PartAt(at);

        // a press keeps the pointer where it began; without one, the part under it decides
        if (_pressTarget == PressTarget.Form || (_pressTarget == PressTarget.None && part == DecorationPart.Client))
        {
            if (_decoration.SetHover(DecorationPart.None)) Invalidate(null);

            SetFrameCursor(null);

            _formHasPointer = true;
            _form.OnPointerMove(ToForm(at), modifiers);

            return;
        }

        LeaveForm();

        if (_decoration.SetHover(part)) Invalidate(null);

        SetFrameCursor(WaylandDecoration.CursorFor(part));
    }

    internal void OnPointerLeave()
    {
        LeaveForm();

        if (_decoration.SetHover(DecorationPart.None)) Invalidate(null);
        if (_decoration.SetPressed(DecorationPart.None)) Invalidate(null);

        // a leave with buttons down is the compositor's grab — a move, a resize, a menu:
        // their releases won't come here
        _pressTarget = PressTarget.None;
        _formButtonsDown = 0;
        _frameCursor = null;
    }

    private void LeaveForm()
    {
        if (!_formHasPointer) return;

        _formHasPointer = false;
        _form.OnPointerLeaveWindow();
    }

    internal void OnPointerButton(MouseButton button, bool pressed, uint serial, KeyModifiers modifiers)
    {
        Point at = _pointerAt;
        DecorationPart part = PartAt(at);

        if (pressed)
        {
            // modality by dropping input, as on X11: the owner of an open dialog is deaf,
            // its frame included
            if (!IsInputEnabled) return;

            if (_pressTarget == PressTarget.Form || (_pressTarget == PressTarget.None && part == DecorationPart.Client))
            {
                _pressTarget = PressTarget.Form;
                _formButtonsDown++;

                _form.OnPointerDown(ToForm(at), button, modifiers);
                return;
            }

            if (_pressTarget == PressTarget.None)
                PressFrame(part, button, serial, at);

            return;
        }

        if (_pressTarget == PressTarget.Form)
        {
            Point formPoint = ToForm(at);

            _form.OnPointerUp(formPoint, button, modifiers);

            // the menu on the release, as on X11
            if (button == MouseButton.Right)
                _form.OnContextMenu(formPoint);

            if (--_formButtonsDown <= 0)
            {
                _formButtonsDown = 0;
                _pressTarget = PressTarget.None;
            }

            return;
        }

        if (_pressTarget == PressTarget.Frame)
        {
            DecorationPart held = _decoration.Pressed;

            if (_decoration.SetPressed(DecorationPart.None)) Invalidate(null);
            _pressTarget = PressTarget.None;

            // a button acts when it is let go over itself, as everywhere
            if (held == part) ActivateButton(part);
        }
    }

    internal void OnPointerWheel(int vertical, int horizontal)
    {
        if (!IsInputEnabled) return;

        // the frame takes no wheel; a press keeps it with the form
        if (_pressTarget == PressTarget.Form || PartAt(_pointerAt) == DecorationPart.Client)
            _form.OnMouseWheel(ToForm(_pointerAt), vertical, horizontal);
    }

    private void PressFrame(DecorationPart part, MouseButton button, uint serial, Point at)
    {
        WlSeat? seat = _platform.Seat?.Proxy;
        if (seat is null || _toplevel is null) return;

        if (part == DecorationPart.Caption)
        {
            switch (button)
            {
                case MouseButton.Left:
                    {
                        long now = Environment.TickCount64;

                        bool isDouble = now - _lastCaptionClickMs < 400
                            && MathF.Abs(at.X - _lastCaptionClickAt.X) < 4
                            && MathF.Abs(at.Y - _lastCaptionClickAt.Y) < 4;

                        if (isDouble)
                        {
                            _lastCaptionClickMs = 0;
                            ToggleMaximized();
                            return;
                        }

                        _lastCaptionClickMs = now;
                        _lastCaptionClickAt = at;

                        // the compositor moves the window: the drag is its from here on
                        _toplevel.Move(seat, serial);
                        return;
                    }

                case MouseButton.Right:
                    {
                        // the compositor's own window menu, at the point in window geometry
                        float band = _decoration.Band;
                        _toplevel.ShowWindowMenu(seat, serial, (int)(at.X - band), (int)(at.Y - band));
                        return;
                    }
            }

            return;
        }

        if (WaylandDecoration.IsButton(part))
        {
            if (button != MouseButton.Left) return;

            _pressTarget = PressTarget.Frame;
            if (_decoration.SetPressed(part)) Invalidate(null);

            return;
        }

        if (button == MouseButton.Left && WaylandDecoration.EdgeOf(part) is { } edge)
            _toplevel.Resize(seat, serial, (uint)edge);
    }

    private void ActivateButton(DecorationPart part)
    {
        switch (part)
        {
            case DecorationPart.Minimize:
                _toplevel?.SetMinimized();

                // the compositor never says a window is minimized: the form is told here
                _form.SetWindowStateFromPlatform(WindowState.Minimized);
                break;

            case DecorationPart.Maximize:
                ToggleMaximized();
                break;

            case DecorationPart.Close:
                OnCloseRequested();
                break;
        }
    }

    private void ToggleMaximized()
    {
        if (_toplevel is null || !_decoration.HasMaximize) return;

        if (_maximized) _toplevel.UnsetMaximized();
        else _toplevel.SetMaximized();
    }

    private void SetFrameCursor(CursorKind? cursor)
    {
        if (_frameCursor == cursor) return;

        _frameCursor = cursor;
        _platform.Seat?.Pointer?.CursorChanged(this);
    }

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
        // the compositor's limits are the window geometry's: the title bar the window
        // draws is added to the form's
        float caption = DrawnFrame?.Caption ?? 0f;

        // 0 — no limit, as NaN is for the form
        _toplevel!.SetMinSize(Limit(minimum.Width, 0), Limit(minimum.Height, caption));
        _toplevel.SetMaxSize(Limit(maximum.Width, 0), Limit(maximum.Height, caption));

        static int Limit(float value, float extra) =>
            float.IsNaN(value) || float.IsInfinity(value) || value <= 0 ? 0 : (int)MathF.Ceiling(value + extra);
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

    public void SetCursor(CursorKind cursor)
    {
        if (CursorPicture is null && CursorKind == cursor) return;

        CursorKind = cursor;
        CursorPicture = null;

        _platform.Seat?.Pointer?.CursorChanged(this);
    }

    public void SetCursor(CursorImage cursor)
    {
        if (ReferenceEquals(CursorPicture, cursor)) return;

        CursorPicture = cursor;
        CursorKind = cursor.Fallback;

        _platform.Seat?.Pointer?.CursorChanged(this);
    }

    /// <summary>While a button is held the compositor sends the pointer to the window it
    /// was pressed in, wherever it goes: Wayland's capture is implicit.</summary>
    public void CaptureMouse()
    {
    }

    public void ReleaseMouseCapture()
    {
    }

    // activation is not here yet: xdg-activation comes with the rest of the protocols
    public void Activate()
    {
    }

    /// <summary>A drag over the window reaches the form: the seat's data device asks
    /// <see cref="AcceptsDrops"/>, there is nothing to register with the compositor.</summary>
    public void SetDragDropEnabled(bool enabled) => AcceptsDrops = enabled;

    /// <summary>The form takes drops: AllowDrop on one of its elements turned it on.</summary>
    internal bool AcceptsDrops { get; private set; }


    /// <summary>The focused text field's caret, in the form's coordinates; null — no field
    /// has the focus.</summary>
    private Rectangle? _textInputArea;

    /// <summary>The input method is on while a text field has the focus, and its windows
    /// open at the caret: the seat's text input is told, see <see cref="WaylandTextInput"/>.</summary>
    public void SetTextInputArea(Rectangle? caret)
    {
        _textInputArea = caret;
        _platform.TextInput?.Update(this);
    }

    /// <summary>The caret on the surface: past the frame the window draws, where it draws one.</summary>
    internal Rectangle? TextInputAreaOnSurface =>
        _textInputArea is { } area && DrawnFrame is { } frame
            ? area.Offset(frame.ContentOrigin.X, frame.ContentOrigin.Y)
            : _textInputArea;
}