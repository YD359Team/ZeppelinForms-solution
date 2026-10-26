using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Linux;

/// <summary>
/// The place of one native control in an X11 window: a child window of the form's, as
/// large as the part of the host the form shows, holding the control at the host's size
/// and offset by what is cut off.
/// </summary>
/// <remarks>
/// The slot listens for the focus: X11 tells the ancestors of the window that takes the
/// keyboard, and a FocusIn here means a click went into the control — the form moves its
/// own focus to the host. The control may belong to another connection, even another
/// process — a player given the slot's XID — and X11 doesn't mind: windows are the
/// server's.
/// </remarks>
internal sealed class X11NativeSlot : INativeControlSlot
{
    private readonly X11Platform _platform;
    private readonly X11Window _owner;
    private readonly nint _display;
    private nuint _window;
    private nuint _control;
    private bool _mapped;

    public X11NativeSlot(X11Platform platform, X11Window owner, nuint parent)
    {
        _platform = platform;
        _owner = owner;
        _display = platform.Display;

        _window = X11.XCreateSimpleWindow(_display, parent, 0, 0, 1, 1, 0, 0, 0);

        // no background of its own: the control covers the slot, and the server
        // doesn't flash a color nobody chose before it does
        X11.XSetWindowBackgroundPixmap(_display, _window, X11.None);
        X11.XSelectInput(_display, _window, X11.FocusChangeMask);

        platform.RegisterSlot(_window, this);
        s_slots[_window] = this;
    }

    public PlatformHandle Parent => new((nint)_window, PlatformHandle.Xid);

    /// <summary>The platform's connection: a host making its window with it.</summary>
    public nint Display => _display;

    public event EventHandler? ControlFocused;

    internal void OnFocusIn() => ControlFocused?.Invoke(this, EventArgs.Empty);

    public void Attach(PlatformHandle control)
    {
        _control = (nuint)control.Handle;

        if (_control == 0 || _window == 0) return;

        // made elsewhere — another connection, a top-level window — or right here:
        // reparenting a child of the slot only puts it at the corner
        X11.XReparentWindow(_display, _control, _window, 0, 0);
        X11.XMapWindow(_display, _control);
        X11.XFlush(_display);
    }

    public void Place(Rectangle bounds, Rectangle visible)
    {
        if (_window == 0) return;

        if (visible.Width <= 0 || visible.Height <= 0)
        {
            if (_mapped)
            {
                X11.XUnmapWindow(_display, _window);
                _mapped = false;
                X11.XFlush(_display);
            }

            return;
        }

        float scale = _owner.Scale;

        // the edges rounded, not the sizes: two hosts side by side meet without a gap
        (int left, int top, int right, int bottom) = Pixels(visible, scale);
        (int controlLeft, int controlTop, int controlRight, int controlBottom) = Pixels(bounds, scale);

        X11.XMoveResizeWindow(_display, _window, left, top,
            (uint)Math.Max(1, right - left), (uint)Math.Max(1, bottom - top));

        if (_control != 0)
        {
            X11.XMoveResizeWindow(_display, _control, controlLeft - left, controlTop - top,
                (uint)Math.Max(1, controlRight - controlLeft), (uint)Math.Max(1, controlBottom - controlTop));
        }

        if (!_mapped)
        {
            X11.XMapWindow(_display, _window);
            _mapped = true;
        }

        X11.XFlush(_display);
    }

    private static (int Left, int Top, int Right, int Bottom) Pixels(Rectangle r, float scale) =>
        ((int)MathF.Round(r.X * scale), (int)MathF.Round(r.Y * scale),
         (int)MathF.Round(r.Right * scale), (int)MathF.Round(r.Bottom * scale));

    public void Focus()
    {
        if (_control == 0 || !_mapped) return;

        X11.XSetInputFocus(_display, _control, X11.RevertToParent, (nuint)X11.CurrentTime);
        X11.XFlush(_display);
    }

    public void DestroyControl(PlatformHandle control)
    {
        if (control.Handle == 0) return;

        X11.XDestroyWindow(_display, (nuint)control.Handle);
        X11.XFlush(_display);

        if ((nuint)control.Handle == _control) _control = 0;
    }

    public void Dispose()
    {
        if (_window == 0) return;

        _platform.UnregisterSlot(_window);
        s_slots.Remove(_window);

        // the form's window may be gone already — its children went with it, and the
        // error handler takes the BadWindow of destroying one again
        X11.XDestroyWindow(_display, _window);
        X11.XFlush(_display);

        _window = 0;
        _control = 0;
    }

    // the slots by their windows, for the hosts given one as their parent
    private static readonly Dictionary<nuint, X11NativeSlot> s_slots = [];

    /// <summary>The slot under a window a host was given as its parent.</summary>
    internal static X11NativeSlot? FromHandle(nuint window) => s_slots.GetValueOrDefault(window);
}