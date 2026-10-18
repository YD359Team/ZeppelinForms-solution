using System.Runtime.InteropServices;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Windows;

/// <summary>The window's size limits, its topmost place and its centering on a screen.</summary>
/// <remarks>
/// The limits are read from the form when the system asks for them — WM_GETMINMAXINFO
/// comes for every drag of an edge, every SetWindowPos and every maximize — so the
/// window always keeps the form's current ones; <see cref="SetSizeLimits"/> only
/// brings a window that is out of new limits back inside them.
/// </remarks>
internal sealed partial class Win32Window
{
    public void SetSizeLimits(Size minimum, Size maximum)
    {
        if (_handle == 0 || !NativeMethods.GetWindowRect(_handle, out NativeMethods.RECT rect)) return;

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;

        // a resize to the size it has already: WM_WINDOWPOSCHANGING, handled by
        // DefWindowProc, asks WM_GETMINMAXINFO for the new limits and holds the
        // window to them — the system does the clamping, frame and DPI included
        NativeMethods.SetWindowPos(
            _handle, 0, 0, 0, width, height,
            NativeConstants.SWP_NOMOVE | NativeConstants.SWP_NOZORDER | NativeConstants.SWP_NOACTIVATE);
    }

    public void SetTopMost(bool topMost)
    {
        if (_handle == 0) return;

        NativeMethods.SetWindowPos(
            _handle,
            topMost ? NativeConstants.HWND_TOPMOST : NativeConstants.HWND_NOTOPMOST,
            0, 0, 0, 0,
            NativeConstants.SWP_NOMOVE | NativeConstants.SWP_NOSIZE | NativeConstants.SWP_NOACTIVATE);
    }

    public void CenterOnScreen()
    {
        if (_handle == 0 || !NativeMethods.GetWindowRect(_handle, out NativeMethods.RECT rect)) return;

        // the screen the window is on, not the primary one: a window on the second
        // monitor centered on the first jumped across the desk
        nint monitor = NativeMethods.MonitorFromWindow(_handle, NativeConstants.MONITOR_DEFAULTTONEAREST);

        var info = new NativeMethods.MONITORINFOEX { cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };

        if (!NativeMethods.GetMonitorInfo(monitor, ref info)) return;

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;

        NativeMethods.RECT work = info.rcWork;

        int x = work.Left + (work.Right - work.Left - width) / 2;
        int y = work.Top + (work.Bottom - work.Top - height) / 2;

        NativeMethods.SetWindowPos(
            _handle, 0, x, y, 0, 0,
            NativeConstants.SWP_NOSIZE | NativeConstants.SWP_NOZORDER | NativeConstants.SWP_NOACTIVATE);
    }

    /// <summary>The window reports its moves and resizes to the form: set once it is
    /// placed, see Create.</summary>
    private bool _reportsBounds;

    /// <summary>Tell the form where the window is and how big it is now. A minimized
    /// window isn't reported: it stands at -32000 off every screen, and the form keeps
    /// the place it will come back to.</summary>
    private void ReportBounds()
    {
        if (!_reportsBounds || _handle == 0 || NativeMethods.IsIconic(_handle)) return;
        if (!NativeMethods.GetWindowRect(_handle, out NativeMethods.RECT rect)) return;

        (int extraWidth, int extraHeight) = FrameExtra();

        _form.OnWindowMoved(new Point(rect.Left, rect.Top));
        _form.OnWindowResized(new Size(
            (rect.Right - rect.Left - extraWidth) / _scale,
            (rect.Bottom - rect.Top - extraHeight) / _scale));
    }

    /// <summary>WM_GETMINMAXINFO: the form's limits over the system's defaults. An axis
    /// with no limit keeps the default — the system's smallest window, the screen.</summary>
    /// <remarks>
    /// The maximum bounds the maximized window as well, as in WinForms: a form at most
    /// 800 wide maximizes to 800 wide in the corner of the screen, not across it.
    /// </remarks>
    private void OnGetMinMaxInfo(nint lParam)
    {
        Size minimum = _form.MinimumSize;
        Size maximum = _form.MaximumSize;

        if (minimum == Size.Auto && maximum == Size.Auto) return;

        var info = Marshal.PtrToStructure<NativeMethods.MINMAXINFO>(lParam);

        if (!minimum.IsWidthAuto) info.ptMinTrackSize.X = Math.Max(info.ptMinTrackSize.X, ToPhysicalWidth(minimum.Width));
        if (!minimum.IsHeightAuto) info.ptMinTrackSize.Y = Math.Max(info.ptMinTrackSize.Y, ToPhysicalHeight(minimum.Height));

        if (!maximum.IsWidthAuto)
        {
            info.ptMaxTrackSize.X = Math.Max(info.ptMinTrackSize.X, ToPhysicalWidth(maximum.Width));
            info.ptMaxSize.X = Math.Min(info.ptMaxSize.X, info.ptMaxTrackSize.X);
        }

        if (!maximum.IsHeightAuto)
        {
            info.ptMaxTrackSize.Y = Math.Max(info.ptMinTrackSize.Y, ToPhysicalHeight(maximum.Height));
            info.ptMaxSize.Y = Math.Min(info.ptMaxSize.Y, info.ptMaxTrackSize.Y);
        }

        Marshal.StructureToPtr(info, lParam, false);
    }

    private int ToPhysicalWidth(float logical) => (int)MathF.Round(logical * _scale) + FrameExtra().Width;

    private int ToPhysicalHeight(float logical) => (int)MathF.Round(logical * _scale) + FrameExtra().Height;

    /// <summary>What the frame adds to a limit. <c>Form.Size</c> is the whole window,
    /// and so are its limits — except for a form sized to its content, whose Size is
    /// the client area (see PlaceAfterCreate): its limits get the frame added, as its
    /// size did.</summary>
    private (int Width, int Height) FrameExtra()
    {
        if (_form.SizeToContent == SizeToContent.None) return (0, 0);

        (uint style, uint exStyle) = ChromeStyles();

        var frame = new NativeMethods.RECT();

        return NativeMethods.AdjustWindowRectExForDpi(ref frame, style, false, exStyle, (uint)MathF.Round(_scale * 96f))
            ? (frame.Right - frame.Left, frame.Bottom - frame.Top)
            : (0, 0);
    }
}