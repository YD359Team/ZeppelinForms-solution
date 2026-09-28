using SkiaSharp;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Linux;

internal sealed class X11Window : IPlatformWindow, IDesktopWindow
{
    private readonly X11Platform _platform;
    private readonly Form _form;
    private readonly ConcurrentQueue<Action> _invokeQueue = new();

    private nint _display;
    private nuint _window;
    private nint _gc;
    private X11SkiaSurface? _surface;

    private nuint _wmDeleteWindow;
    private nuint _invokeAtom;

    /// <summary>The size of the last ConfigureNotify, in physical pixels.
    /// ConfigureNotify comes for moves too, and a move changes nothing inside.</summary>
    private int _physicalWidth;
    private int _physicalHeight;

    public nuint Handle => _window;
    public nuint InvokeAtom => _invokeAtom;

    private float _scale = 1f;

    public float Scale => _scale;

    public bool SupportsTransparency
    {
        get
        {
            int screen = X11.XDefaultScreen(_display);
            nuint selection = X11.XInternAtom(_display, $"_NET_WM_CM_S{screen}", false);

            // the selection has an owner — so a compositor is running
            return X11.XGetSelectionOwner(_display, selection) != 0;
        }
    }

    private X11DropTarget? _dropTarget;

    public void SetDragDropEnabled(bool enabled)
    {
        if (enabled == (_dropTarget is not null)) return;

        if (enabled)
        {
            _dropTarget = new X11DropTarget(_display, _window, _form, ToClient);
            _dropTarget.Register();

            return;
        }

        // receiving may never have been enabled: then there is nothing to disable
        _dropTarget?.Unregister();
        _dropTarget = null;
    }

    internal X11DropTarget? DropTarget => _dropTarget;

    /// <summary>XDND gives screen coordinates, while routing in Form works in client
    /// ones already divided by the scale. The conversion goes through the server:
    /// it knows both the window's position and the current decorations.</summary>
    private Point ToClient(Point screen)
    {
        nuint root = X11.XRootWindow(_display, X11.XDefaultScreen(_display));

        X11.XTranslateCoordinates(_display, root, _window,
            (int)screen.X, (int)screen.Y, out int x, out int y, out _);

        return new Point(x / Scale, y / Scale);
    }

    public X11Window(X11Platform platform, Form form)
    {
        _platform = platform;
        _form = form;
        Frames = new X11FrameDriver(platform, this);
    }

    public IFrameDriver Frames { get; }

    internal bool IsInputEnabled { get; private set; } = true;

    public void SetEnabled(bool enabled) => IsInputEnabled = enabled;

    public void Activate()
    {
        if (_window == 0) return;

        X11.XRaiseWindow(_display, _window);
        X11.XSetInputFocus(_display, _window, X11.RevertToParent, (nuint)X11.CurrentTime);
        X11.XFlush(_display);
    }

    public void Create()
    {
        if (_window != 0) return;

        _display = _platform.Display;
        int screen = X11.XDefaultScreen(_display);

        _scale = X11Dpi.GetScale(_display, screen);
        Displays.Current = new X11DisplayProvider(_display, _scale, X11Dpi.GetDpi(_display, screen));

        _window = X11.XCreateSimpleWindow(
            _display, X11.XRootWindow(_display, screen),
            (int)(_form.Position.X * _scale), (int)(_form.Position.Y * _scale),
            (uint)Math.Max(1, _form.Size.Width * _scale),
            (uint)Math.Max(1, _form.Size.Height * _scale),
            0, 0, X11.XWhitePixel(_display, screen));

        X11.XSelectInput(_display, _window,
            X11.ExposureMask | X11.StructureNotifyMask
            | X11.KeyPressMask | X11.KeyReleaseMask
            | X11.ButtonPressMask | X11.ButtonReleaseMask
            | X11.PointerMotionMask | X11.LeaveWindowMask | X11.FocusChangeMask);

        // without this the close button in the title bar simply kills the connection to the server
        _wmDeleteWindow = X11.XInternAtom(_display, "WM_DELETE_WINDOW", false);
        X11.XSetWMProtocols(_display, _window, [_wmDeleteWindow], 1);

        _invokeAtom = X11.XInternAtom(_display, "ZF_INVOKE", false);

        _gc = X11.XDefaultGC(_display, screen);

        _surface = new X11SkiaSurface(
            _display, _window, _gc,
            X11.XDefaultVisual(_display, screen),
            (uint)X11.XDefaultDepth(_display, screen));

        X11.XStoreName(_display, _window, _form.Title ?? string.Empty);
        _platform.Register(this);
    }

    public void Show()
    {
        Create();
        X11.XMapWindow(_display, _window);
        X11.XFlush(_display);
    }

    public void Close()
    {
        if (_window == 0) return;

        _surface?.Dispose();
        _surface = null;

        X11.XDestroyWindow(_display, _window);
        X11.XFlush(_display);

        _platform.Unregister(this);
        _window = 0;

        _form.OnWindowClosed();
    }

    public void SetTitle(string? title)
    {
        if (_window != 0)
            X11.XStoreName(_display, _window, title ?? string.Empty);
    }

    public void SetBounds(Rectangle bounds) =>
        X11.XMoveResizeWindow(_display, _window,
            (int)bounds.X, (int)bounds.Y,
            (uint)Math.Max(1, bounds.Width), (uint)Math.Max(1, bounds.Height));

    // a repaint has been requested and not done yet; _fullRepaint — the whole
    // window, otherwise the accumulated _pendingDirty
    private bool _paintRequested;
    private bool _fullRepaint;
    private Rectangle? _pendingDirty;

    internal bool IsPaintRequested => _paintRequested;

    /// <summary>Mark an area dirty. The frame is painted by the platform loop,
    /// once, after all pending events are handled.</summary>
    /// <remarks>
    /// X11 has no deferred repaint of its own, so this used to paint right away:
    /// every Invalidate drew a whole frame, and one user action produces dozens of
    /// them. A handler that invalidated in the middle of changing its state had that
    /// half-updated state painted. Now the area is only accumulated here, and
    /// X11Platform.PumpOnce paints it at the end of the pump — the same thing
    /// WM_PAINT does on Windows.
    /// </remarks>
    public void Invalidate(Rectangle? bounds = null)
    {
        if (bounds is null)
        {
            _fullRepaint = true;
            _pendingDirty = null;
        }
        else if (!_fullRepaint)
        {
            _pendingDirty = _pendingDirty is { } existing
                ? existing.Union(bounds.Value)
                : bounds;
        }

        _paintRequested = true;
    }

    /// <summary>Paint what was requested since the last frame, if anything.</summary>
    internal void PaintIfRequested()
    {
        if (!_paintRequested) return;

        Paint(_fullRepaint ? null : _pendingDirty);
    }

    internal void Paint(Rectangle? dirty = null)
    {
        // the request is reset before drawing, not after: drawing may request
        // the next frame — an animation does exactly that — and that request
        // must survive until the next pump
        _paintRequested = false;
        _fullRepaint = false;
        _pendingDirty = null;

        if (_surface?.BeginFrame() is SKSurface skSurface)
        {
            Skia.SkiaRenderer.Render(_form, skSurface.Canvas, _scale, dirty);
            _surface.EndFrame(dirty is { } d ? ToPhysical(d) : null);
        }

        _form.TakeDirtyRegion();
    }

    public void Invoke(Action action)
    {
        _invokeQueue.Enqueue(action);
        _platform.WakeUp(this);
    }

    public void SetOpacity(float opacity)
    {
        if (_window == 0) return;

        nuint property = X11.XInternAtom(_display, "_NET_WM_WINDOW_OPACITY", false);

        // a fully opaque window is the absence of the property rather than its
        // maximum value: this way the compositor doesn't spend a pass on a window
        // that needs no blending
        if (opacity >= 1f)
        {
            X11.XDeleteProperty(_display, _window, property);
            X11.XFlush(_display);

            return;
        }

        uint value = (uint)(Math.Clamp(opacity, 0f, 1f) * uint.MaxValue);

        X11.XChangeProperty(_display, _window, property, X11.XA_CARDINAL, 32,
            X11.PropModeReplace, BitConverter.GetBytes(value), 1);

        X11.XFlush(_display);
    }

    public void SetWindowState(WindowState state)
    {
        nuint netWmState = X11.XInternAtom(_display, "_NET_WM_STATE", false);
        nuint maxHorz = X11.XInternAtom(_display, "_NET_WM_STATE_MAXIMIZED_HORZ", false);
        nuint maxVert = X11.XInternAtom(_display, "_NET_WM_STATE_MAXIMIZED_VERT", false);

        switch (state)
        {
            case WindowState.Minimized:
                // minimizing is the only thing done by a call
                // rather than a message to the manager
                X11.XIconifyWindow(_display, _window, X11.XDefaultScreen(_display));
                break;

            case WindowState.Maximized:
                SendState(X11.NetWmStateAdd, maxHorz, maxVert);
                break;

            case WindowState.Normal:
                // XMapWindow brings it back from minimized, removing both flags —
                // from maximized. We do both: we don't know which state
                // the window was in
                X11.XMapWindow(_display, _window);
                SendState(X11.NetWmStateRemove, maxHorz, maxVert);
                break;
        }

        X11.XFlush(_display);

        void SendState(int action, nuint first, nuint second)
        {
            var message = new X11.XClientMessageEvent
            {
                type = X11.ClientMessage,
                display = _display,
                window = _window,
                message_type = netWmState,
                format = 32,
                data0 = action,
                data1 = (nint)first,
                data2 = (nint)second,
                // the source is an ordinary application, not a panel or a pager
                data3 = 1,
            };

            nint buffer = Marshal.AllocHGlobal(Marshal.SizeOf<X11.XClientMessageEvent>());

            try
            {
                Marshal.StructureToPtr(message, buffer, false);

                // the message is addressed to the root window: it's not us but
                // the window manager that maximizes, and it listens on the root
                nuint root = X11.XRootWindow(_display, X11.XDefaultScreen(_display));

                X11.XSendEvent(_display, root, false,
                    X11.SubstructureNotifyMask | X11.SubstructureRedirectMask, buffer);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    private readonly Dictionary<CursorKind, nuint> _cursorCache = [];
    private CursorKind _currentCursor = CursorKind.Default;

    public void SetCursor(CursorKind cursor)
    {
        if (_window == 0 || cursor == _currentCursor) return;

        _currentCursor = cursor;

        if (!_cursorCache.TryGetValue(cursor, out nuint handle))
        {
            handle = X11.XCreateFontCursor(_display, ToXShape(cursor));
            _cursorCache[cursor] = handle;
        }

        X11.XDefineCursor(_display, _window, handle);
        X11.XFlush(_display);
    }

    // codes from X11/cursorfont.h
    private static uint ToXShape(CursorKind cursor) => cursor switch
    {
        CursorKind.Hand => 60,              // XC_hand2
        CursorKind.IBeam => 152,            // XC_xterm
        CursorKind.Wait => 150,             // XC_watch
        CursorKind.SizeWestEast => 108,     // XC_sb_h_double_arrow
        CursorKind.SizeNorthSouth => 116,   // XC_sb_v_double_arrow
        CursorKind.SizeAll => 52,           // XC_fleur
        CursorKind.Cross => 34,             // XC_crosshair
        CursorKind.No => 88,                // XC_pirate
        _ => 68,                            // XC_left_ptr
    };

    internal void DrainInvokeQueue()
    {
        while (_invokeQueue.TryDequeue(out Action? action))
            action();
    }

    // the dirty area comes in logical coordinates, while XPutImage
    // copies the buffer's physical pixels
    private Rectangle ToPhysical(Rectangle logical) => new(
        new Point(logical.X * _scale, logical.Y * _scale),
        new Size(logical.Width * _scale, logical.Height * _scale));

    internal void HandleConfigure(int width, int height)
    {
        // ConfigureNotify comes for moves as well. A move used to cost a full layout
        // and a synchronous frame; a window that only moved changes nothing inside
        if (width == _physicalWidth && height == _physicalHeight) return;

        _physicalWidth = width;
        _physicalHeight = height;

        _surface?.Resize(width, height);                                // the surface is physical
        _form.ClientSize = new Size(width / _scale, height / _scale);   // the tree is logical
        _form.PerformLayout();

        Invalidate(null);
    }

    internal bool IsDeleteMessage(nuint atom) => atom == _wmDeleteWindow;

    internal void RaiseTick() => _form.Tick();

    internal Form Form => _form;

    public void CaptureMouse()
    {
        int result = X11.XGrabPointer(
            _display,
            (nint)_window,
            ownerEvents: false,
            X11.ButtonPressMask | X11.ButtonReleaseMask | X11.PointerMotionMask,
            X11.GrabModeAsync,
            X11.GrabModeAsync,
            confineTo: X11.NoneHandle,
            cursor: X11.NoneHandle,
            X11.CurrentTime);

        // the grab may already be taken by another client — an open window manager
        // menu, for example. Then the drag goes as before: it works until the cursor
        // leaves the window, after that the button sticks
        Debug.Assert(result == X11.GrabSuccess, $"XGrabPointer returned {result}");
    }

    public void ReleaseMouseCapture() => X11.XUngrabPointer(_display, X11.CurrentTime);
}