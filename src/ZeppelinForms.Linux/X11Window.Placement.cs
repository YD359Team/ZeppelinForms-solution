using System.Runtime.InteropServices;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Linux;

/// <summary>
/// The window's size limits, its topmost place, its centering on a screen, the place it opens at and its hiding.
/// </summary>
/// <remarks>
/// All of it is a request to the window manager, as the frame is (see X11Window.Chrome):
/// the limits go as WM_NORMAL_HINTS, the topmost place as <c>_NET_WM_STATE_ABOVE</c>.
/// Sizes here are the client area — X11 knows no other — as <c>Form.Size</c> is on Linux.
/// </remarks>
internal sealed partial class X11Window
{
    public void SetSizeLimits(Size minimum, Size maximum)
    {
        if (_window == 0) return;

        ApplySizeHints();

        // the hints bound the user's dragging only: a window already out of the new
        // limits is brought inside by hand
        if (_physicalWidth > 0 && _physicalHeight > 0)
        {
            Size logical = _form.ClampToSizeLimits(new Size(_physicalWidth / _scale, _physicalHeight / _scale));

            int width = Math.Max(1, (int)MathF.Round(logical.Width * _scale));
            int height = Math.Max(1, (int)MathF.Round(logical.Height * _scale));

            if (width != _physicalWidth || height != _physicalHeight)
                X11.XResizeWindow(_display, _window, (uint)width, (uint)height);
        }

        X11.XFlush(_display);
    }

    /// <summary>Form.Hide withdraws the window, Form.Show maps it again.</summary>
    private bool _hidden;

    public void SetVisible(bool visible)
    {
        if (_window == 0 || _hidden == !visible) return;

        _hidden = !visible;

        if (!visible)
        {
            X11.XWithdrawWindow(_display, _window, X11.XDefaultScreen(_display));
            X11.XFlush(_display);
            return;
        }

        // the manager forgets a withdrawn window's state: the frame, the topmost place
        // and the taskbar's say go again before it is mapped, as for a new window
        ApplyChrome(mapped: false);

        X11.XMapWindow(_display, _window);

        // asked of the manager rather than taken with XSetInputFocus: the window isn't
        // viewable until the manager has framed it, and the focus can't go to it before
        SendRootMessage("_NET_ACTIVE_WINDOW", data0: 1);

        // what was asked while it was hidden is drawn now, whole
        Invalidate(null);

        X11.XFlush(_display);
    }

    public void SetTopMost(bool topMost)
    {
        if (_window == 0) return;

        SendNetWmState(topMost ? X11.NetWmStateAdd : X11.NetWmStateRemove,
            X11.XInternAtom(_display, "_NET_WM_STATE_ABOVE", false));

        X11.XFlush(_display);
    }

    public void CenterOnScreen()
    {
        if (_window == 0) return;

        nuint root = X11.XRootWindow(_display, X11.XDefaultScreen(_display));
        X11.XTranslateCoordinates(_display, _window, root, 0, 0, out int left, out int top, out _);

        int width = _physicalWidth > 0 ? _physicalWidth : (int)(_form.Size.Width * _scale);
        int height = _physicalHeight > 0 ? _physicalHeight : (int)(_form.Size.Height * _scale);

        // the screen the window is on: the one its middle is over
        Rectangle area = ScreenAt(left + width / 2, top + height / 2);

        X11.XMoveWindow(_display, _window,
            (int)(area.X + (area.Width - width) / 2),
            (int)(area.Y + (area.Height - height) / 2));

        X11.XFlush(_display);
    }

    /// <summary>The size the window opens at, in pixels: the form's size kept within
    /// its limits — the hints bound only the user's dragging, not XCreateWindow.</summary>
    private (uint Width, uint Height) OpeningSize()
    {
        Size size = _form.ClampToSizeLimits(_form.Size);

        return ((uint)Math.Max(1, size.Width * _scale), (uint)Math.Max(1, size.Height * _scale));
    }

    /// <summary>Where the window opens, in pixels; null — the manager's choice, as
    /// <see cref="WindowStartupLocation.Default"/> asks.</summary>
    /// <remarks>Until 0.17 every window was created at Form.Position and the manager
    /// placed it as it liked: CenterScreen and CenterOwner did nothing on Linux.</remarks>
    private (int X, int Y)? OpeningPosition()
    {
        (uint width, uint height) = OpeningSize();

        switch (_form.WindowStartupLocation)
        {
            // Form.Position is in screen pixels, as Displays has the screens; until 0.17
            // Linux took it as logical and multiplied it by the scale, unlike Windows
            case WindowStartupLocation.Manual:
                return ((int)_form.Position.X, (int)_form.Position.Y);

            case WindowStartupLocation.CenterOwner
                when _form.Owner?.PlatformWindow is X11Window { _window: not 0 } owner:
                {
                    nuint root = X11.XRootWindow(_display, X11.XDefaultScreen(_display));
                    X11.XTranslateCoordinates(_display, owner._window, root, 0, 0, out int left, out int top, out _);

                    return (left + (owner._physicalWidth - (int)width) / 2,
                        top + (owner._physicalHeight - (int)height) / 2);
                }

            // CenterOwner without an owner centers on the screen, as on Windows
            case WindowStartupLocation.CenterScreen or WindowStartupLocation.CenterOwner:
                {
                    Rectangle area = Displays.Primary.WorkingArea;

                    return ((int)(area.X + (area.Width - width) / 2), (int)(area.Y + (area.Height - height) / 2));
                }

            default:
                return null;
        }
    }

    /// <summary>The working area of the screen a point is on, in pixels; the primary
    /// screen's when it is on none.</summary>
    private static Rectangle ScreenAt(int x, int y)
    {
        foreach (DisplayInfo display in Displays.All)
        {
            Rectangle bounds = display.Bounds;

            if (x >= bounds.X && x < bounds.X + bounds.Width && y >= bounds.Y && y < bounds.Y + bounds.Height)
                return display.WorkingArea;
        }

        return Displays.Primary.WorkingArea;
    }

    /// <summary>Ask the manager to add or remove a state of a mapped window. Before the
    /// window is mapped the state is the property itself (see SetSkipTaskbar).</summary>
    private void SendNetWmState(int action, nuint state) =>
        // the source is an ordinary application, not a panel or a pager
        SendRootMessage("_NET_WM_STATE", data0: action, data1: (nint)state, data3: 1);

    /// <summary>A request about this window to the window manager, which listens on
    /// the root window.</summary>
    private void SendRootMessage(string type, nint data0 = 0, nint data1 = 0, nint data2 = 0, nint data3 = 0)
    {
        var message = new X11.XClientMessageEvent
        {
            type = X11.ClientMessage,
            display = _display,
            window = _window,
            message_type = X11.XInternAtom(_display, type, false),
            format = 32,
            data0 = data0,
            data1 = data1,
            data2 = data2,
            data3 = data3,
        };

        nint buffer = Marshal.AllocHGlobal(Marshal.SizeOf<X11.XClientMessageEvent>());

        try
        {
            Marshal.StructureToPtr(message, buffer, false);

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