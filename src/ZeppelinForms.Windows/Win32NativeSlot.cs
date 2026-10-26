using System.ComponentModel;
using System.Runtime.InteropServices;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Windows;

/// <summary>A message the hosted window sent its parent: <c>WM_COMMAND</c> of a button,
/// <c>WM_NOTIFY</c> of a list view, <c>WM_CTLCOLOR*</c> of an edit. Set
/// <paramref name="handled"/> to answer it with the value returned; otherwise the
/// default handling answers.</summary>
public delegate nint HwndMessageHook(nint hwnd, uint message, nint wParam, nint lParam, ref bool handled);

/// <summary>
/// The place of one native control in a Win32 window: a child window of the form's, as
/// large as the part of the host the form shows, holding the control at the host's size
/// and offset by what is cut off.
/// </summary>
/// <remarks>
/// <para>
/// The slot is the control's parent, so what a standard control tells its parent —
/// <c>WM_COMMAND</c>, <c>WM_NOTIFY</c>, the <c>WM_CTLCOLOR</c> family — comes here and goes
/// on to <see cref="MessageHook"/>: <c>HwndHost.WndProc</c>, as in WPF.
/// </para>
/// <para>
/// A press in the control comes as <c>WM_PARENTNOTIFY</c> before the control takes the
/// keyboard: the form moves its own focus to the host then. The form's window takes the
/// keyboard back on a press of its own, see <c>Win32Window.TakeKeyboardBack</c>.
/// </para>
/// </remarks>
internal sealed class Win32NativeSlot : INativeControlSlot
{
    private const string ClassName = "ZeppelinForms.NativeSlot";

    private static readonly NativeMethods.WndProc s_wndProc = WndProc;
    private static bool s_registered;

    // the slots by their windows, for the hosts given one as their parent
    private static readonly Dictionary<nint, Win32NativeSlot> s_slots = [];

    private readonly Win32Window _owner;
    private GCHandle _self;
    private nint _handle;
    private nint _control;

    public Win32NativeSlot(Win32Window owner, nint parent)
    {
        _owner = owner;

        Register();

        _self = GCHandle.Alloc(this);

        // a child of the form's window, hidden until placed. Clipping its siblings and
        // children: two slots that overlap don't paint into each other
        _handle = NativeMethods.CreateWindowEx(
            0, ClassName, string.Empty,
            NativeConstants.WS_CHILD | NativeConstants.WS_CLIPSIBLINGS | NativeConstants.WS_CLIPCHILDREN,
            0, 0, 0, 0, parent, 0,
            NativeMethods.GetModuleHandle(null),
            GCHandle.ToIntPtr(_self));

        if (_handle == 0)
        {
            int error = Marshal.GetLastWin32Error();
            _self.Free();
            throw new Win32Exception(error);
        }

        s_slots[_handle] = this;
    }

    public PlatformHandle Parent => new(_handle, PlatformHandle.Hwnd);

    /// <summary>The host's handler of what the control tells its parent.</summary>
    public HwndMessageHook? MessageHook { get; set; }

    public event EventHandler? ControlFocused;

    public void Attach(PlatformHandle control)
    {
        _control = control.Handle;

        if (_control == 0 || !NativeMethods.IsWindow(_control)) return;

        // a window made elsewhere — a top-level one, another library's — becomes the
        // slot's child: a child's style, no frame, and the slot as its parent
        if (NativeMethods.GetParent(_control) != _handle)
        {
            nint style = NativeMethods.GetWindowLongPtr(_control, NativeConstants.GWL_STYLE);
            nint frame = unchecked((nint)(NativeConstants.WS_POPUP | NativeConstants.WS_CAPTION | NativeConstants.WS_THICKFRAME));

            NativeMethods.SetWindowLongPtr(_control, NativeConstants.GWL_STYLE, (style & ~frame) | (nint)NativeConstants.WS_CHILD);
            NativeMethods.SetParent(_control, _handle);
        }

        NativeMethods.ShowWindow(_control, NativeConstants.SW_SHOWNA);
    }

    public void Place(Rectangle bounds, Rectangle visible)
    {
        if (_handle == 0) return;

        if (visible.Width <= 0 || visible.Height <= 0)
        {
            NativeMethods.ShowWindow(_handle, NativeConstants.SW_HIDE);
            return;
        }

        float scale = _owner.Scale;

        // the edges rounded, not the sizes: two hosts side by side meet without a gap
        (int left, int top, int right, int bottom) = Pixels(visible, scale);
        (int controlLeft, int controlTop, int controlRight, int controlBottom) = Pixels(bounds, scale);

        NativeMethods.SetWindowPos(
            _handle, NativeConstants.HWND_TOP, left, top, right - left, bottom - top,
            NativeConstants.SWP_NOACTIVATE | NativeConstants.SWP_SHOWWINDOW);

        if (_control != 0)
        {
            NativeMethods.SetWindowPos(
                _control, 0, controlLeft - left, controlTop - top, controlRight - controlLeft, controlBottom - controlTop,
                NativeConstants.SWP_NOZORDER | NativeConstants.SWP_NOACTIVATE);
        }
    }

    private static (int Left, int Top, int Right, int Bottom) Pixels(Rectangle r, float scale) =>
        ((int)MathF.Round(r.X * scale), (int)MathF.Round(r.Y * scale),
         (int)MathF.Round(r.Right * scale), (int)MathF.Round(r.Bottom * scale));

    public void Focus()
    {
        if (_control != 0 && NativeMethods.IsWindow(_control))
            NativeMethods.SetFocus(_control);
    }

    public void DestroyControl(PlatformHandle control)
    {
        if (control.Handle != 0 && NativeMethods.IsWindow(control.Handle))
            NativeMethods.DestroyWindow(control.Handle);

        if (control.Handle == _control) _control = 0;
    }

    public void Dispose()
    {
        s_slots.Remove(_handle);

        if (_handle != 0 && NativeMethods.IsWindow(_handle))
            NativeMethods.DestroyWindow(_handle);

        _handle = 0;
        _control = 0;
        MessageHook = null;

        if (_self.IsAllocated) _self.Free();
    }

    /// <summary>The slot of a handle a host was given as its parent: how HwndHost finds
    /// the slot to hook its messages.</summary>
    internal static Win32NativeSlot? FromHandle(nint handle) =>
        s_slots.GetValueOrDefault(handle);

    private static void Register()
    {
        if (s_registered) return;

        var windowClass = new NativeMethods.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
            lpfnWndProc = s_wndProc,
            hInstance = NativeMethods.GetModuleHandle(null),
            hCursor = NativeMethods.LoadCursor(0, NativeConstants.IDC_ARROW),
            lpszClassName = ClassName,
        };

        if (NativeMethods.RegisterClassEx(ref windowClass) == 0)
        {
            int error = Marshal.GetLastWin32Error();

            // ERROR_CLASS_ALREADY_EXISTS
            if (error != 1410) throw new Win32Exception(error);
        }

        s_registered = true;
    }

    private static nint WndProc(nint hWnd, uint message, nint wParam, nint lParam)
    {
        if (message == NativeConstants.WM_NCCREATE)
            NativeMethods.SetWindowLongPtr(hWnd, NativeConstants.GWLP_USERDATA, Marshal.ReadIntPtr(lParam));

        // the slot itself, from the window's user data: it is set before the first
        // message that matters, and this class has no windows but slots
        nint data = NativeMethods.GetWindowLongPtr(hWnd, NativeConstants.GWLP_USERDATA);

        if (data == 0 || GCHandle.FromIntPtr(data).Target is not Win32NativeSlot slot)
            return NativeMethods.DefWindowProc(hWnd, message, wParam, lParam);

        try
        {
            return slot.ProcessMessage(hWnd, message, wParam, lParam);
        }
        catch (Exception exception) when (App.HasUnhandledExceptionHandler)
        {
            if (!App.OfferUnhandledException(exception)) throw;

            return 0;
        }
    }

    private nint ProcessMessage(nint hWnd, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            // the control covers the slot; what it doesn't cover isn't erased to
            // a color nobody chose — and nothing flickers on a resize
            case NativeConstants.WM_ERASEBKGND:
                return 1;

            case NativeConstants.WM_PARENTNOTIFY:
                {
                    uint what = (uint)(wParam.ToInt64() & 0xFFFF);

                    if (what is NativeConstants.WM_LBUTTONDOWN or NativeConstants.WM_RBUTTONDOWN or
                        NativeConstants.WM_MBUTTONDOWN or NativeConstants.WM_XBUTTONDOWN or NativeConstants.WM_POINTERDOWN)
                        ControlFocused?.Invoke(this, EventArgs.Empty);

                    break;
                }

            case NativeConstants.WM_NCDESTROY:
                NativeMethods.SetWindowLongPtr(hWnd, NativeConstants.GWLP_USERDATA, 0);
                return NativeMethods.DefWindowProc(hWnd, message, wParam, lParam);
        }

        if (MessageHook is { } hook)
        {
            bool handled = false;
            nint result = hook(hWnd, message, wParam, lParam, ref handled);

            if (handled) return result;
        }

        return NativeMethods.DefWindowProc(hWnd, message, wParam, lParam);
    }
}