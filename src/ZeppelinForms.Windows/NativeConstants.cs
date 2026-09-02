namespace ZeppelinForms.Windows;

internal static class NativeConstants
{
    public const int MONITOR_DEFAULTTONEAREST = 2;
    public const uint MONITORINFOF_PRIMARY = 1;
    public const uint WM_SETCURSOR = 0x0020;
    public const int IDC_ARROW = 32512;
    public const int IDC_IBEAM = 32513;
    public const int IDC_WAIT = 32514;
    public const int IDC_CROSS = 32515;
    public const int IDC_SIZEWE = 32644;
    public const int IDC_SIZENS = 32645;
    public const int IDC_SIZEALL = 32646;
    public const int IDC_NO = 32648;
    public const int IDC_HAND = 32649;
    public const int CFS_POINT = 0x0002;
    public const uint WM_TIMER = 0x0113;
    public const nuint AnimationTimerId = 1;
    public const uint WM_RBUTTONDOWN = 0x0204;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint CF_UNICODETEXT = 13;
    public const uint GMEM_MOVEABLE = 0x0002;
    public const int SWP_NOMOVE = 0x0002;
    public const uint WS_EX_LAYERED = 0x00080000;
    public const uint LWA_ALPHA = 0x00000002;
    public const int GWL_EXSTYLE = -20;
    public const int SW_MINIMIZE = 6;
    public const int SW_MAXIMIZE = 3;
    public const int SW_RESTORE = 9;
    public const uint WS_MINIMIZEBOX = 0x00020000;
    public const uint WS_MAXIMIZEBOX = 0x00010000;
    public const uint WS_THICKFRAME = 0x00040000;
    public const int SIZE_RESTORED = 0;
    public const int SIZE_MINIMIZED = 1;
    public const int SIZE_MAXIMIZED = 2;
    public const uint WM_DPICHANGED = 0x02E0;
    public const int SWP_NOZORDER = 0x0004;
    public const int SWP_NOACTIVATE = 0x0010;
    // PER_MONITOR_AWARE_V2
    public static readonly nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;
    public const int VK_SHIFT = 0x10;
    public const int VK_CONTROL = 0x11;
    public const int VK_MENU = 0x12;
    public const uint WM_CHAR = 0x0102;
    public const int CW_USEDEFAULT = unchecked((int)0x80000000);
    public const int SM_CXSCREEN = 0;
    public const int SM_CYSCREEN = 1;
    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_USER = 0x0400;
    public const uint WM_INVOKE = WM_USER + 1;
    public const uint WM_MOUSEMOVE = 0x0200;
    public const uint WM_MOUSEWHEEL = 0x020A;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_MOUSELEAVE = 0x02A3;
    public const uint TME_LEAVE = 0x00000002;
    public const uint WM_PAINT = 0x000F;
    public const uint WM_SIZE = 0x0005;
    public const uint WM_ERASEBKGND = 0x0014;
    public const uint WM_SETICON = 0x0080;
    public static readonly nint ICON_SMALL = 0;
    public static readonly nint ICON_BIG = 1;
    public const uint PFD_DRAW_TO_WINDOW = 0x4;
    public const uint PFD_SUPPORT_OPENGL = 0x20;
    public const uint PFD_DOUBLEBUFFER = 0x1;
    public const byte PFD_TYPE_RGBA = 0;
    public const byte PFD_MAIN_PLANE = 0;
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
    public const uint IMAGE_ICON = 1;
    public const uint LR_LOADFROMFILE = 0x00000010;
    public const uint LR_DEFAULTSIZE = 0x00000040;
    public const uint LR_SHARED = 0x00008000;
    public const uint BI_RGB = 0;
    public const uint DIB_RGB_COLORS = 0;
    public const uint SRCCOPY = 0x00CC0020;
}