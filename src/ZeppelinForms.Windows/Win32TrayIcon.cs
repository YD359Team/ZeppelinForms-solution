using System.ComponentModel;
using System.Runtime.InteropServices;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;

namespace ZeppelinForms.Windows;

/// <summary>
/// A <see cref="TrayIcon"/> in the notification area: Shell_NotifyIcon, version 4, with
/// a hidden window of its own for the shell's messages and the system's popup menu.
/// </summary>
/// <remarks>
/// <para>
/// The window is a hidden top-level one, not a message-only window: Explorer broadcasts
/// <c>TaskbarCreated</c> when it restarts, a broadcast reaches no message-only window, and
/// the icon would be gone from the tray until the application restarted.
/// </para>
/// <para>
/// The menu is built from <see cref="TrayIcon.Menu"/> each time it opens and shown with
/// TrackPopupMenuEx. The window is made the foreground one first and sent WM_NULL after,
/// as the documentation of TrackPopupMenu asks: without both the menu doesn't close on a
/// click elsewhere, or closes at once the second time.
/// </para>
/// </remarks>
internal sealed class Win32TrayIcon : ITrayIconImpl
{
    private const string ClassName = "ZeppelinForms.TrayIcon";

    // the shell's notifications come to the window as this message
    private const uint CallbackMessage = NativeConstants.WM_USER + 0x100;

    private static readonly NativeMethods.WndProc s_wndProc = WndProc;
    private static readonly uint s_taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private static bool s_registered;
    private static uint s_nextId;

    private readonly TrayIcon _owner;
    private readonly uint _id = ++s_nextId;
    private GCHandle _self;
    private nint _window;
    private nint _icon;

    public Win32TrayIcon(TrayIcon owner)
    {
        _owner = owner;

        Register();

        _self = GCHandle.Alloc(this);

        // WS_POPUP, never shown: a top-level window, so that TaskbarCreated reaches it
        _window = NativeMethods.CreateWindowEx(
            0, ClassName, string.Empty, NativeConstants.WS_POPUP,
            0, 0, 0, 0, 0, 0, NativeMethods.GetModuleHandle(null), GCHandle.ToIntPtr(_self));

        if (_window == 0)
        {
            int error = Marshal.GetLastWin32Error();
            _self.Free();
            throw new Win32Exception(error);
        }

        Add();
    }

    // ===== the icon =====

    private NOTIFYICONDATAW Data(uint flags)
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _window,
            uID = _id,
            uFlags = flags,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon,
            szTip = Truncate(_owner.ToolTip, 127),
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
        };

        return data;
    }

    private static string Truncate(string? text, int length) =>
        text is null ? string.Empty : text.Length <= length ? text : text[..length];

    private void Add()
    {
        ReplaceIcon();

        NOTIFYICONDATAW data = Data(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);

        if (!Shell_NotifyIcon(NIM_ADD, ref data)) return;

        // version 4: NIN_SELECT for a click, WM_CONTEXTMENU for the menu, the place in wParam
        data.uTimeoutOrVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIcon(NIM_SETVERSION, ref data);
    }

    public void Update()
    {
        if (_window == 0) return;

        ReplaceIcon();

        NOTIFYICONDATAW data = Data(NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    /// <summary>The owner's picture at the size of the tray's icons, as an HICON; the
    /// previous one goes once the shell holds the new one.</summary>
    private void ReplaceIcon()
    {
        int size = NativeMethods.GetSystemMetrics(SM_CXSMICON);
        nint previous = _icon;

        _icon = Win32Icon.Create(_owner.EffectiveIcon, size, size);

        if (previous != 0) NativeMethods.DestroyIcon(previous);
    }

    public void Dispose()
    {
        if (_window == 0) return;

        NOTIFYICONDATAW data = Data(0);
        Shell_NotifyIcon(NIM_DELETE, ref data);

        NativeMethods.DestroyWindow(_window);
        _window = 0;

        if (_icon != 0)
        {
            NativeMethods.DestroyIcon(_icon);
            _icon = 0;
        }

        if (_self.IsAllocated) _self.Free();
    }

    // ===== the shell's messages =====

    private nint ProcessMessage(nint hWnd, uint message, nint wParam, nint lParam)
    {
        if (message == CallbackMessage)
        {
            // version 4: the event in the low word of lParam, the place in wParam
            uint what = (uint)(lParam.ToInt64() & 0xFFFF);

            switch (what)
            {
                case NIN_SELECT:
                case NIN_KEYSELECT:
                    _owner.RaiseClick();
                    break;

                case WM_LBUTTONDBLCLK:
                    _owner.RaiseDoubleClick();
                    break;

                case WM_CONTEXTMENU:
                    ShowMenu((short)(wParam.ToInt64() & 0xFFFF), (short)((wParam.ToInt64() >> 16) & 0xFFFF));
                    break;
            }

            return 0;
        }

        // Explorer restarted: the tray is a new one, and the icon goes into it again
        if (message == s_taskbarCreated && s_taskbarCreated != 0)
        {
            Add();
            return 0;
        }

        return NativeMethods.DefWindowProc(hWnd, message, wParam, lParam);
    }

    // ===== the menu =====

    private void ShowMenu(int x, int y)
    {
        _owner.RaiseMenuOpening();

        if (_owner.Menu.Count == 0) return;

        var items = new List<MenuItem>();
        nint menu = BuildMenu(_owner.Menu, items);

        try
        {
            NativeMethods.SetForegroundWindow(_window);

            int chosen = TrackPopupMenuEx(
                menu, TPM_RETURNCMD | TPM_NONOTIFY | TPM_RIGHTBUTTON | TPM_BOTTOMALIGN, x, y, _window, 0);

            NativeMethods.PostMessage(_window, WM_NULL, 0, 0);

            // the ids are the items' places in the list, from 1: 0 is "nothing chosen"
            if (chosen > 0 && chosen <= items.Count)
                items[chosen - 1].Invoke();
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    /// <summary>A popup menu of the items, submenus included; every item that can be
    /// chosen gets an id — its place in <paramref name="chosen"/>, from 1.</summary>
    private static nint BuildMenu(List<MenuItem> source, List<MenuItem> chosen)
    {
        nint menu = CreatePopupMenu();

        foreach (MenuItem item in source)
        {
            if (item.IsSeparator)
            {
                AppendMenu(menu, MF_SEPARATOR, 0, null);
                continue;
            }

            uint flags = MF_STRING;
            if (!item.IsEffectivelyEnabled) flags |= MF_GRAYED;
            if (item.IsEffectivelyChecked) flags |= MF_CHECKED;

            if (item.Items.Count > 0)
            {
                // a submenu's items are its own business: Opening first, as a menu bar does
                item.RaiseOpening();

                nint submenu = BuildMenu(item.Items, chosen);
                AppendMenu(menu, flags | MF_POPUP, (nuint)submenu, item.Text);
                continue;
            }

            chosen.Add(item);
            AppendMenu(menu, flags, (nuint)chosen.Count, item.Text);
        }

        return menu;
    }

    // ===== the window class =====

    private static void Register()
    {
        if (s_registered) return;

        var windowClass = new NativeMethods.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
            lpfnWndProc = s_wndProc,
            hInstance = NativeMethods.GetModuleHandle(null),
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

        nint data = NativeMethods.GetWindowLongPtr(hWnd, NativeConstants.GWLP_USERDATA);

        if (data == 0 || GCHandle.FromIntPtr(data).Target is not Win32TrayIcon icon)
            return NativeMethods.DefWindowProc(hWnd, message, wParam, lParam);

        if (message == NativeConstants.WM_NCDESTROY)
        {
            NativeMethods.SetWindowLongPtr(hWnd, NativeConstants.GWLP_USERDATA, 0);
            return NativeMethods.DefWindowProc(hWnd, message, wParam, lParam);
        }

        try
        {
            return icon.ProcessMessage(hWnd, message, wParam, lParam);
        }
        catch (Exception exception) when (App.HasUnhandledExceptionHandler)
        {
            if (!App.OfferUnhandledException(exception)) throw;

            return 0;
        }
    }

    // ===== Win32 =====

    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_SHOWTIP = 0x80;
    private const uint NOTIFYICON_VERSION_4 = 4;
    private const uint NIN_SELECT = 0x0400, NIN_KEYSELECT = 0x0401;
    private const uint WM_CONTEXTMENU = 0x007B, WM_LBUTTONDBLCLK = 0x0203, WM_NULL = 0x0000;
    private const int SM_CXSMICON = 49;

    private const uint MF_STRING = 0x0, MF_GRAYED = 0x1, MF_CHECKED = 0x8, MF_POPUP = 0x10, MF_SEPARATOR = 0x800;
    private const uint TPM_RIGHTBUTTON = 0x2, TPM_BOTTOMALIGN = 0x20, TPM_NONOTIFY = 0x80, TPM_RETURNCMD = 0x100;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATAW data);

    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(nint menu, uint flags, nuint idOrSubmenu, string? text);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint window, nint parameters);
}