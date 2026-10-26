using System.ComponentModel;
using System.Runtime.InteropServices;
using ZeppelinForms.Forms.Controls;

namespace ZeppelinForms.Windows;

/// <summary>
/// A Win32 window among the form's elements, as WPF's <c>HwndHost</c> hosts one: a legacy
/// control, a video renderer, a third-party view that wants an HWND.
/// <see cref="BuildWindowCore"/> makes the window as a child of the parent it is given,
/// or makes it elsewhere and returns it — it becomes the parent's child either way.
/// </summary>
/// <remarks>
/// <para>
/// The window is laid out with the host: placed, sized, cut by a scrolled panel round it,
/// hidden with a hidden page, all in physical pixels of the window's DPI. What it tells its
/// parent — <c>WM_COMMAND</c>, <c>WM_NOTIFY</c>, <c>WM_CTLCOLOR*</c> — comes to
/// <see cref="WndProc"/> and <see cref="MessageHook"/>.
/// </para>
/// <para>
/// See <see cref="NativeControlHost"/> for what any native control can't do in a form:
/// the form's popups open under it, and Tab leads into it but not out of it.
/// </para>
/// </remarks>
public abstract class HwndHost : NativeControlHost
{
    private Win32NativeSlot? _slot;

    /// <summary>The hosted window; 0 before it is built and after it is destroyed.</summary>
    public nint Handle { get; private set; }

    /// <summary>What the hosted window tells its parent, for code that doesn't derive a
    /// host of its own; <see cref="WndProc"/> sees it first.</summary>
    public event HwndMessageHook? MessageHook;

    /// <summary>Make the window, a child of <paramref name="hwndParent"/>, and return it.</summary>
    protected abstract nint BuildWindowCore(nint hwndParent);

    /// <summary>Destroy the window <see cref="BuildWindowCore"/> made. The default is
    /// DestroyWindow.</summary>
    protected virtual void DestroyWindowCore(nint hwnd)
    {
        if (hwnd != 0) DestroyWindow(hwnd);
    }

    /// <summary>A message the hosted window sent its parent. Set
    /// <paramref name="handled"/> to answer it with the value returned.</summary>
    protected virtual nint WndProc(nint hwnd, uint message, nint wParam, nint lParam, ref bool handled) => 0;

    protected sealed override PlatformHandle CreateNativeControlCore(PlatformHandle parent)
    {
        if (parent.Kind != PlatformHandle.Hwnd)
            throw new PlatformNotSupportedException($"An HwndHost lives in a Win32 window, not in a {parent.Kind} one.");

        _slot = Win32NativeSlot.FromHandle(parent.Handle);

        if (_slot is not null) _slot.MessageHook = OnMessage;

        nint hwnd = BuildWindowCore(parent.Handle);

        if (hwnd == 0)
            throw new InvalidOperationException($"{GetType().Name}.BuildWindowCore returned no window.");

        Handle = hwnd;
        return new PlatformHandle(hwnd, PlatformHandle.Hwnd);
    }

    protected sealed override void DestroyNativeControlCore(PlatformHandle control)
    {
        if (_slot is not null) _slot.MessageHook = null;
        _slot = null;

        try
        {
            DestroyWindowCore(control.Handle);
        }
        finally
        {
            Handle = 0;
        }
    }

    private nint OnMessage(nint hwnd, uint message, nint wParam, nint lParam, ref bool handled)
    {
        nint result = WndProc(hwnd, message, wParam, lParam, ref handled);

        if (handled) return result;

        return MessageHook is { } hook ? hook(hwnd, message, wParam, lParam, ref handled) : 0;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint hWnd);
}

/// <summary>
/// A standard Win32 control by its class name — <c>EDIT</c>, <c>BUTTON</c>,
/// <c>SysListView32</c>, <c>SysMonthCal32</c> — or any registered class: the shortest way
/// to put one into a form. Its notifications come to <see cref="HwndHost.MessageHook"/>.
/// </summary>
public class Win32ControlHost : HwndHost
{
    /// <summary>The window class: a system one or one registered by the application.</summary>
    public required string ClassName { get; init; }

    /// <summary>Styles beyond WS_CHILD and WS_VISIBLE, which the host adds itself.</summary>
    public uint Style { get; init; }

    public uint ExStyle { get; init; }

    /// <summary>The window's text: an edit's contents, a button's caption.</summary>
    public string? WindowText { get; init; }

    protected override nint BuildWindowCore(nint hwndParent)
    {
        const uint Child = 0x40000000, Visible = 0x10000000;

        nint hwnd = CreateWindowEx(
            ExStyle, ClassName, WindowText ?? string.Empty, Style | Child | Visible,
            0, 0, 0, 0, hwndParent, 0, GetModuleHandle(null), 0);

        return hwnd != 0 ? hwnd : throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(
        uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);
}