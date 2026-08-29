using SkiaSharp;
using System.Collections.Concurrent;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Linux;

internal sealed class X11Window : IPlatformWindow
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

    public nuint Handle => _window;
    public nuint InvokeAtom => _invokeAtom;

    public X11Window(X11Platform platform, Form form)
    {
        _platform = platform;
        _form = form;
    }

    public void Create()
    {
        if (_window != 0) return;

        _display = _platform.Display;
        int screen = X11.XDefaultScreen(_display);

        _window = X11.XCreateSimpleWindow(
            _display, X11.XRootWindow(_display, screen),
            (int)_form.Position.X, (int)_form.Position.Y,
            (uint)Math.Max(1, _form.Size.Width), (uint)Math.Max(1, _form.Size.Height),
            0, 0, X11.XWhitePixel(_display, screen));

        X11.XSelectInput(_display, _window,
            X11.ExposureMask | X11.StructureNotifyMask
            | X11.KeyPressMask | X11.KeyReleaseMask
            | X11.ButtonPressMask | X11.ButtonReleaseMask
            | X11.PointerMotionMask | X11.LeaveWindowMask | X11.FocusChangeMask);

        // без этого крестик в заголовке просто убьёт соединение с сервером
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

    public void Invalidate(Rectangle? bounds = null)
    {
        // в X11 нет InvalidateRect — рисуем сразу, синхронно
        Paint();
    }

    public void Invoke(Action action)
    {
        _invokeQueue.Enqueue(action);
        _platform.WakeUp(this);
    }

    public void SetOpacity(float opacity)
    {
        // требует _NET_WM_WINDOW_OPACITY и работающего композитора — TODO
    }

    public void SetWindowState(WindowState state)
    {
        // требует _NET_WM_STATE / XIconifyWindow — TODO
    }

    internal void DrainInvokeQueue()
    {
        while (_invokeQueue.TryDequeue(out Action? action))
            action();
    }

    internal void Paint()
    {
        if (_surface?.BeginFrame() is SKSurface skSurface)
        {
            Skia.SkiaRenderer.Render(_form, skSurface.Canvas);
            _surface.EndFrame();
        }
    }

    internal void HandleConfigure(int width, int height)
    {
        _surface?.Resize(width, height);
        _form.ClientSize = new Size(width, height);
        _form.PerformLayout();
        Paint();
    }

    internal bool IsDeleteMessage(nuint atom) => atom == _wmDeleteWindow;

    internal Form Form => _form;
}