using System.Runtime.InteropServices;

namespace ZeppelinForms.Windows;

internal static class NativeMethods
{
    [DllImport("user32.dll")]
    public static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

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

    [DllImport("user32.dll")]
    public static extern nint SendMessage(
    nint hWnd,
    uint message,
    nint wParam,
    nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct PAINTSTRUCT
    {
        public nint hdc;
        public bool fErase;
        public RECT rcPaint;
        public bool fRestore;
        public bool fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] rgbReserved;
    }

    [DllImport("user32.dll")]
    public static extern nint BeginPaint(nint hWnd, out PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")]
    public static extern bool EndPaint(nint hWnd, ref PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")]
    public static extern nint GetDC(nint hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(nint hWnd, nint hDC);

    // --- GDI (software путь) ---


    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [DllImport("gdi32.dll")]
    public static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll")]
    public static extern nint CreateDIBSection(
        nint hdc, ref BITMAPINFOHEADER pbmi, uint usage,
        out nint ppvBits, nint hSection, uint offset);

    [DllImport("gdi32.dll")]
    public static extern nint SelectObject(nint hdc, nint hObject);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(nint hObject);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(nint hdc);

    [DllImport("gdi32.dll")]
    public static extern bool BitBlt(
        nint hdcDest, int xDest, int yDest, int width, int height,
        nint hdcSrc, int xSrc, int ySrc, uint rop);

    // --- WGL (hardware путь) ---

    [StructLayout(LayoutKind.Sequential)]
    public struct PIXELFORMATDESCRIPTOR
    {
        public ushort nSize;
        public ushort nVersion;
        public uint dwFlags;
        public byte iPixelType;
        public byte cColorBits;
        public byte cRedBits, cRedShift, cGreenBits, cGreenShift;
        public byte cBlueBits, cBlueShift, cAlphaBits, cAlphaShift;
        public byte cAccumBits, cAccumRedBits, cAccumGreenBits, cAccumBlueBits, cAccumAlphaBits;
        public byte cDepthBits, cStencilBits, cAuxBuffers;
        public byte iLayerType;
        public byte bReserved;
        public uint dwLayerMask, dwVisibleMask, dwDamageMask;
    }

    [DllImport("gdi32.dll")]
    public static extern int ChoosePixelFormat(nint hdc, ref PIXELFORMATDESCRIPTOR pfd);

    [DllImport("gdi32.dll")]
    public static extern bool SetPixelFormat(nint hdc, int format, ref PIXELFORMATDESCRIPTOR pfd);

    [DllImport("gdi32.dll")]
    public static extern bool SwapBuffers(nint hdc);

    [DllImport("opengl32.dll")]
    public static extern nint wglCreateContext(nint hdc);

    [DllImport("opengl32.dll")]
    public static extern bool wglMakeCurrent(nint hdc, nint hglrc);

    [DllImport("opengl32.dll")]
    public static extern bool wglDeleteContext(nint hglrc);

    [DllImport("user32.dll")]
    public static extern bool InvalidateRect(nint hWnd, nint lpRect, bool bErase);

    [StructLayout(LayoutKind.Sequential)]
    public struct TRACKMOUSEEVENT
    {
        public uint cbSize;
        public uint dwFlags;
        public nint hwndTrack;
        public uint dwHoverTime;
    }

    [DllImport("user32.dll")]
    public static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT lpEventTrack);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    public static extern bool ScreenToClient(nint hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);
}
