using System.Runtime.InteropServices;

namespace ZeppelinForms.Linux;

internal static class X11
{
    private const string Lib = "libX11.so.6";

    // маски событий
    public const long KeyPressMask = 1L << 0;
    public const long KeyReleaseMask = 1L << 1;
    public const long ButtonPressMask = 1L << 2;
    public const long ButtonReleaseMask = 1L << 3;
    public const long PointerMotionMask = 1L << 6;
    public const long LeaveWindowMask = 1L << 5;
    public const long ExposureMask = 1L << 15;
    public const long StructureNotifyMask = 1L << 17;
    public const long FocusChangeMask = 1L << 21;

    // типы событий
    public const int KeyPress = 2;
    public const int KeyRelease = 3;
    public const int ButtonPress = 4;
    public const int ButtonRelease = 5;
    public const int MotionNotify = 6;
    public const int LeaveNotify = 8;
    public const int Expose = 12;
    public const int ConfigureNotify = 22;
    public const int ClientMessage = 33;

    // модификаторы в поле state
    public const uint ShiftMask = 1 << 0;
    public const uint ControlMask = 1 << 2;
    public const uint Mod1Mask = 1 << 3;   // Alt

    [DllImport(Lib)] public static extern nint XOpenDisplay(nint display);
    [DllImport(Lib)] public static extern int XCloseDisplay(nint display);
    [DllImport(Lib)] public static extern int XDefaultScreen(nint display);
    [DllImport(Lib)] public static extern nuint XRootWindow(nint display, int screen);
    [DllImport(Lib)] public static extern nuint XWhitePixel(nint display, int screen);
    [DllImport(Lib)] public static extern nint XDefaultVisual(nint display, int screen);
    [DllImport(Lib)] public static extern int XDefaultDepth(nint display, int screen);
    [DllImport(Lib)] public static extern nint XDefaultGC(nint display, int screen);

    [DllImport(Lib)]
    public static extern nuint XCreateSimpleWindow(
        nint display, nuint parent, int x, int y,
        uint width, uint height, uint borderWidth, nuint border, nuint background);

    [DllImport(Lib)] public static extern int XDestroyWindow(nint display, nuint window);
    [DllImport(Lib)] public static extern int XMapWindow(nint display, nuint window);
    [DllImport(Lib)] public static extern int XUnmapWindow(nint display, nuint window);
    [DllImport(Lib)] public static extern int XSelectInput(nint display, nuint window, long mask);
    [DllImport(Lib)] public static extern int XStoreName(nint display, nuint window, string name);
    [DllImport(Lib)] public static extern int XFlush(nint display);
    [DllImport(Lib)] public static extern int XNextEvent(nint display, nint eventReturn);
    [DllImport(Lib)] public static extern int XPending(nint display);
    [DllImport(Lib)] public static extern int XMoveResizeWindow(nint display, nuint window, int x, int y, uint width, uint height);

    [DllImport(Lib)] public static extern nuint XInternAtom(nint display, string name, bool onlyIfExists);
    [DllImport(Lib)] public static extern int XSetWMProtocols(nint display, nuint window, nuint[] protocols, int count);

    [DllImport(Lib)]
    public static extern int XSendEvent(nint display, nuint window, bool propagate, long mask, nint sendEvent);

    [DllImport(Lib)]
    public static extern nint XCreateImage(
        nint display, nint visual, uint depth, int format, int offset,
        nint data, uint width, uint height, int bitmapPad, int bytesPerLine);

    [DllImport(Lib)]
    public static extern int XPutImage(
        nint display, nuint drawable, nint gc, nint image,
        int srcX, int srcY, int destX, int destY, uint width, uint height);

    [DllImport(Lib)] public static extern nuint XLookupKeysym(nint keyEvent, int index);

    [DllImport(Lib)]
    public static extern int XLookupString(nint keyEvent, byte[] buffer, int bufferSize, out nuint keysym, nint status);

    [StructLayout(LayoutKind.Sequential)]
    public struct XKeyEvent
    {
        public int type;
        public nuint serial;
        public int send_event;
        public nint display;
        public nuint window;
        public nuint root;
        public nuint subwindow;
        public nuint time;
        public int x, y;
        public int x_root, y_root;
        public uint state;
        public uint keycode;
        public int same_screen;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XButtonEvent
    {
        public int type;
        public nuint serial;
        public int send_event;
        public nint display;
        public nuint window;
        public nuint root;
        public nuint subwindow;
        public nuint time;
        public int x, y;
        public int x_root, y_root;
        public uint state;
        public uint button;
        public int same_screen;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XMotionEvent
    {
        public int type;
        public nuint serial;
        public int send_event;
        public nint display;
        public nuint window;
        public nuint root;
        public nuint subwindow;
        public nuint time;
        public int x, y;
        public int x_root, y_root;
        public uint state;
        public byte is_hint;
        public int same_screen;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XConfigureEvent
    {
        public int type;
        public nuint serial;
        public int send_event;
        public nint display;
        public nuint eventWindow;
        public nuint window;
        public int x, y;
        public int width, height;
        public int border_width;
        public nuint above;
        public int override_redirect;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XClientMessageEvent
    {
        public int type;
        public nuint serial;
        public int send_event;
        public nint display;
        public nuint window;
        public nuint message_type;
        public int format;
        public nint data0;
        public nint data1;
        public nint data2;
        public nint data3;
        public nint data4;
    }

    [DllImport(Lib)] public static extern int XDestroyImage(nint image);

    public const int SelectionRequest = 30;
    public const int SelectionNotify = 31;
    public const int SelectionClear = 29;
    public const int PropertyNotify = 28;

    public const int PropModeReplace = 0;
    public const long PropertyChangeMask = 1L << 22;

    [DllImport(Lib)] public static extern nuint XGetSelectionOwner(nint display, nuint selection);
    [DllImport(Lib)] public static extern int XSetSelectionOwner(nint display, nuint selection, nuint owner, nuint time);
    [DllImport(Lib)] public static extern int XConvertSelection(nint display, nuint selection, nuint target, nuint property, nuint requestor, nuint time);

    [DllImport(Lib)]
    public static extern int XChangeProperty(
        nint display, nuint window, nuint property, nuint type, int format,
        int mode, byte[] data, int elements);

    [DllImport(Lib)]
    public static extern int XGetWindowProperty(
        nint display, nuint window, nuint property, long offset, long length,
        bool delete, nuint requestedType, out nuint actualType, out int actualFormat,
        out nuint itemCount, out nuint bytesAfter, out nint data);

    [DllImport(Lib)] public static extern int XFree(nint data);

    [StructLayout(LayoutKind.Sequential)]
    public struct XSelectionRequestEvent
    {
        public int type;
        public nuint serial;
        public int send_event;
        public nint display;
        public nuint owner;
        public nuint requestor;
        public nuint selection;
        public nuint target;
        public nuint property;
        public nuint time;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XSelectionEvent
    {
        public int type;
        public nuint serial;
        public int send_event;
        public nint display;
        public nuint requestor;
        public nuint selection;
        public nuint target;
        public nuint property;
        public nuint time;
    }

    [DllImport(Lib)] public static extern int XConnectionNumber(nint display);

    [DllImport("libc", SetLastError = true)]
    public static extern int select(int nfds, ref FdSet readfds, nint writefds, nint exceptfds, ref TimeVal timeout);

    [StructLayout(LayoutKind.Sequential)]
    public struct TimeVal
    {
        public nint Seconds;
        public nint Microseconds;
    }

    // fd_set в glibc — битовая маска на 1024 дескриптора (16 машинных слов по 64 бита)
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct FdSet
    {
        private fixed long _bits[16];

        public void Clear()
        {
            for (int i = 0; i < 16; i++)
                _bits[i] = 0;
        }

        public void Set(int fd)
        {
            _bits[fd / 64] |= 1L << (fd % 64);
        }

        public readonly bool IsSet(int fd)
        {
            fixed (long* bits = _bits)
                return (bits[fd / 64] & (1L << (fd % 64))) != 0;
        }
    }
}
