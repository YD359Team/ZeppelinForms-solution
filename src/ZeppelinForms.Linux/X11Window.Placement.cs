using System.Runtime.InteropServices;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Linux;

/// <summary>The window's size limits, its topmost place, its centering on a screen and
/// the place it opens at.</summary>
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
            case WindowStartupLocation.Manual:
                return ((int)(_form.Position.X * _scale), (int)(_form.Position.Y * _scale));

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
    private void SendNetWmState(int action, nuint state)
    {
        var message = new X11.XClientMessageEvent
        {
            type = X11.ClientMessage,
            display = _display,
            window = _window,
            message_type = X11.XInternAtom(_display, "_NET_WM_STATE", false),
            format = 32,
            data0 = action,
            data1 = (nint)state,
            data2 = 0,
            // the source is an ordinary application, not a panel or a pager
            data3 = 1,
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