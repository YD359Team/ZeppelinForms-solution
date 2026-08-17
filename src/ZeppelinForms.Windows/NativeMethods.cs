using System.Runtime.InteropServices;

namespace ZeppelinForms.Windows;

internal static class NativeMethods
{
    public const uint WS_OVERLAPPEDWINDOW =
        0x00000000 |
        0x00C00000 |
        0x00080000 |
        0x00040000 |
        0x00020000 |
        0x00010000;

    public const int SW_SHOW = 5;

    public const uint WM_NCCREATE = 0x0081;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_NCDESTROY = 0x0082;

    public const int GWLP_USERDATA = -21;

    public const int IDC_ARROW = 32512;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate nint WndProc(
        nint hWnd,
        uint message,
        nint wParam,
        nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;

        public WndProc lpfnWndProc;

        public int cbClsExtra;
        public int cbWndExtra;

        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszMenuName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszClassName;

        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct CREATESTRUCT
    {
        public nint lpCreateParams;
        public nint hInstance;
        public nint hMenu;
        public nint hwndParent;

        public int cy;
        public int cx;
        public int y;
        public int x;

        public nint style;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszClass;

        public uint dwExStyle;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport(
        "user32.dll",
        EntryPoint = "RegisterClassExW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    public static extern ushort RegisterClassEx(
        ref WNDCLASSEX windowClass);

    [DllImport(
        "user32.dll",
        EntryPoint = "CreateWindowExW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    public static extern nint CreateWindowEx(
        uint exStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint param);

    [DllImport(
        "user32.dll",
        EntryPoint = "DefWindowProcW")]
    public static extern nint DefWindowProc(
        nint hWnd,
        uint message,
        nint wParam,
        nint lParam);

    [DllImport("user32.dll")]
    public static extern bool DestroyWindow(
        nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(
        nint hWnd,
        int command);

    [DllImport("user32.dll")]
    public static extern bool UpdateWindow(
        nint hWnd);

    [DllImport(
        "user32.dll",
        EntryPoint = "SetWindowTextW",
        CharSet = CharSet.Unicode)]
    public static extern bool SetWindowText(
        nint hWnd,
        string text);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(
        nint hWnd,
        nint insertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    [DllImport(
        "user32.dll",
        EntryPoint = "GetMessageW")]
    public static extern int GetMessage(
        out MSG msg,
        nint hWnd,
        uint minFilter,
        uint maxFilter);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(
        ref MSG msg);

    [DllImport(
        "user32.dll",
        EntryPoint = "DispatchMessageW")]
    public static extern nint DispatchMessage(
        ref MSG msg);

    [DllImport("user32.dll")]
    public static extern void PostQuitMessage(
        int exitCode);

    [DllImport(
        "kernel32.dll",
        EntryPoint = "GetModuleHandleW",
        CharSet = CharSet.Unicode)]
    public static extern nint GetModuleHandle(
        string? moduleName);

    [DllImport("user32.dll")]
    public static extern nint LoadCursor(
        nint instance,
        nint cursor);

    [DllImport(
        "user32.dll",
        EntryPoint = "SetWindowLongPtrW")]
    public static extern nint SetWindowLongPtr(
        nint hWnd,
        int index,
        nint value);

    [DllImport(
        "user32.dll",
        EntryPoint = "GetWindowLongPtrW")]
    public static extern nint GetWindowLongPtr(
        nint hWnd,
        int index);

    public const uint IMAGE_ICON = 1;

    public const uint LR_LOADFROMFILE = 0x00000010;
    public const uint LR_DEFAULTSIZE = 0x00000040;
    public const uint LR_SHARED = 0x00008000;

    [DllImport(
        "user32.dll",
        EntryPoint = "LoadImageW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    public static extern nint LoadImage(
        nint instance,
        string name,
        uint type,
        int cx,
        int cy,
        uint flags);

    [DllImport(
    "user32.dll",
    EntryPoint = "CreateIconFromResourceEx",
    SetLastError = true)]
    public static extern nint CreateIconFromResourceEx(
    nint presbits,
    uint dwResSize,
    bool fIcon,
    uint dwVer,
    int cxDesired,
    int cyDesired,
    uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(
    nint hIcon);

    public const uint WM_SETICON = 0x0080;

    public static readonly nint ICON_SMALL = 0;
    public static readonly nint ICON_BIG = 1;

    [DllImport("user32.dll")]
    public static extern nint SendMessage(
    nint hWnd,
    uint message,
    nint wParam,
    nint lParam);
}