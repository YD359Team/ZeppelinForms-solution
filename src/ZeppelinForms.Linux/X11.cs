using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ZeppelinForms.Linux;

internal static class X11
{
    private const string Lib = "libX11.so.6";

    public const int RevertToParent = 2;

    // window state is managed through the window manager: we have no right to
    // change our own properties, the desired state is reported with a message
    public const int NetWmStateRemove = 0;
    public const int NetWmStateAdd = 1;

    public const nuint XA_CARDINAL = 6;

    public const long SubstructureNotifyMask = 1L << 19;
    public const long SubstructureRedirectMask = 1L << 20;

    [DllImport(Lib)] public static extern int XIconifyWindow(nint display, nuint window, int screen);

    [DllImport(Lib)]
    public static extern int XTranslateCoordinates(
      nint display, nuint srcWindow, nuint destWindow,
      int srcX, int srcY, out int destX, out int destY, out nuint child);

    public const nuint None = 0;

    public const nuint XA_ATOM = 4;

    [DllImport(Lib)] public static extern int XDeleteProperty(nint display, nuint window, nuint property);

    // grab modes and special values
    public const int GrabModeSync = 0;
    public const int GrabModeAsync = 1;

    public const int GrabSuccess = 0;

    /// <summary>CurrentTime: the server substitutes its current time.</summary>
    public const nint CurrentTime = 0;

    /// <summary>None: "don't change the cursor", "no window given".</summary>
    public const nint NoneHandle = 0;

    // event masks
    public const long KeyPressMask = 1L << 0;
    public const long KeyReleaseMask = 1L << 1;
    public const long ButtonPressMask = 1L << 2;
    public const long ButtonReleaseMask = 1L << 3;
    public const long PointerMotionMask = 1L << 6;
    public const long LeaveWindowMask = 1L << 5;
    public const long ExposureMask = 1L << 15;
    public const long StructureNotifyMask = 1L << 17;
    public const long FocusChangeMask = 1L << 21;

    // event types
    public const int KeyPress = 2;
    public const int KeyRelease = 3;
    public const int ButtonPress = 4;
    public const int ButtonRelease = 5;
    public const int MotionNotify = 6;
    public const int LeaveNotify = 8;
    public const int Expose = 12;
    public const int ConfigureNotify = 22;
    public const int ClientMessage = 33;

    // modifiers in the state field
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
    [DllImport(Lib)] public static extern int XRaiseWindow(nint display, nuint window);
    [DllImport(Lib)] public static extern int XSetInputFocus(nint display, nuint focus, int revertTo, nuint time);
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

    /// <summary>The fields every X event starts with. Enough to find the window
    /// for events whose own fields are not needed — Expose, FocusIn, FocusOut.</summary>
    /// <remarks>
    /// Expose used to be read through XConfigureEvent. That struct has an extra
    /// "event" field before "window", so its window field fell on Expose's x and y,
    /// no window was ever found, and Expose was ignored.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public struct XAnyEvent
    {
        public int type;
        public nuint serial;
        public int send_event;
        public nint display;
        public nuint window;
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
    public const int FocusOut = 10;

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


    /// <summary>XSizeHints: the size limits the window manager keeps the window
    /// within. PMinSize and PMaxSize equal — a window that can't be resized.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct XSizeHints
    {
        public nint flags;
        public int x, y, width, height;
        public int min_width, min_height;
        public int max_width, max_height;
        public int width_inc, height_inc;
        public int min_aspect_x, min_aspect_y;
        public int max_aspect_x, max_aspect_y;
        public int base_width, base_height;
        public int win_gravity;
    }

    public const nint PMinSize = 1 << 4;
    public const nint PMaxSize = 1 << 5;

    [DllImport(Lib)] public static extern void XSetWMNormalHints(nint display, nuint window, ref XSizeHints hints);

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

    // fd_set in glibc is a bit mask for 1024 descriptors (16 machine words of 64 bits)
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

    [DllImport(Lib)] public static extern nint XResourceManagerString(nint display);
    [DllImport(Lib)] public static extern void XrmInitialize();
    [DllImport(Lib)] public static extern nint XrmGetStringDatabase(string data);
    [DllImport(Lib)] public static extern void XrmDestroyDatabase(nint database);

    [DllImport(Lib)]
    public static extern bool XrmGetResource(
    nint database, string name, string className,
    out nint type, out XrmValue value);

    [DllImport(Lib)] public static extern int XDisplayWidth(nint display, int screen);
    [DllImport(Lib)] public static extern int XDisplayHeight(nint display, int screen);
    [DllImport(Lib)] public static extern int XDisplayWidthMM(nint display, int screen);
    [DllImport(Lib)] public static extern int XDisplayHeightMM(nint display, int screen);

    [StructLayout(LayoutKind.Sequential)]
    public struct XrmValue
    {
        public uint Size;
        public nint Address;
    }

    [DllImport(Lib)] public static extern nuint XCreateFontCursor(nint display, uint shape);
    [DllImport(Lib)] public static extern int XDefineCursor(nint display, nuint window, nuint cursor);

    private const string RandrLib = "libXrandr.so.2";

    [DllImport(RandrLib)] public static extern nint XRRGetScreenResourcesCurrent(nint display, nuint window);
    [DllImport(RandrLib)] public static extern void XRRFreeScreenResources(nint resources);
    [DllImport(RandrLib)] public static extern nint XRRGetCrtcInfo(nint display, nint resources, nuint crtc);
    [DllImport(RandrLib)] public static extern void XRRFreeCrtcInfo(nint info);
    [DllImport(RandrLib)] public static extern nuint XRRGetOutputPrimary(nint display, nuint window);

    [StructLayout(LayoutKind.Sequential)]
    public struct XRRScreenResources
    {
        public nuint timestamp;
        public nuint configTimestamp;
        public int ncrtc;
        public nint crtcs;
        public int noutput;
        public nint outputs;
        public int nmode;
        public nint modes;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XRRCrtcInfo
    {
        public nuint timestamp;
        public int x, y;
        public uint width, height;
        public nuint mode;
        public ushort rotation;
        public int noutput;
        public nint outputs;
        public ushort rotations;
        public int npossible;
        public nint possible;
    }

    [DllImport("libX11.so.6")]
    public static extern int XGrabPointer(
    nint display, nint window, bool ownerEvents, long eventMask,
    int pointerMode, int keyboardMode, nint confineTo, nint cursor, nint time);


    [DllImport(Lib)]
    public static extern int XGrabPointer(
    nint display,
    nuint window,
    bool ownerEvents,
    long eventMask,
    int pointerMode,
    int keyboardMode,
    nuint confineTo,
    nuint cursor,
    nint time);

    [DllImport(Lib)]
    public static extern int XUngrabPointer(nint display, nint time);

    // ===== the input method: text input beyond Latin-1 =====
    // XLookupString gives Latin-1 bytes, and for a Cyrillic or any other
    // non-Latin key it gives nothing at all. Xutf8LookupString through an input
    // context gives UTF-8 for every layout, and the input method also handles
    // dead keys and Compose

    public const int FocusIn = 9;

    public const int LC_ALL = 6;

    public const nint XIMPreeditNothing = 0x0008;
    public const nint XIMStatusNothing = 0x0400;

    public const int XBufferOverflow = -1;
    public const int XLookupChars = 2;
    public const int XLookupBoth = 4;

    // "libc" is mapped by the runtime to the platform's libc.so.6
    [DllImport("libc", EntryPoint = "setlocale")]
    public static extern nint SetLocale(int category, string locale);

    [DllImport(Lib)] public static extern nint XSetLocaleModifiers(string modifiers);

    [DllImport(Lib)] public static extern nint XOpenIM(nint display, nint database, nint resourceName, nint resourceClass);

    [DllImport(Lib)] public static extern int XCloseIM(nint inputMethod);

    // XCreateIC is variadic. A fixed-arity binding for exactly the arguments passed
    // works on x64 and arm64 Linux: they are all pointer-sized and go in registers
    [DllImport(Lib)]
    public static extern nint XCreateIC(
        nint inputMethod,
        string inputStyleName, nint inputStyle,
        string clientWindowName, nuint clientWindow,
        string focusWindowName, nuint focusWindow,
        nint terminator);

    [DllImport(Lib)] public static extern void XDestroyIC(nint inputContext);

    [DllImport(Lib)] public static extern void XSetICFocus(nint inputContext);

    [DllImport(Lib)] public static extern void XUnsetICFocus(nint inputContext);

    [DllImport(Lib)]
    public static extern int Xutf8LookupString(
        nint inputContext, nint keyEvent, byte[] buffer, int bufferSize,
        out nuint keysym, out int status);

    /// <summary>Let the input method see the event first. true — it took the event
    /// (a dead key, a Compose sequence), and it must not be dispatched.</summary>
    [DllImport(Lib)] public static extern bool XFilterEvent(nint xevent, nuint window);

    /// <summary>Take out of the queue only an event of this type for this window,
    /// without blocking; the other events stay where they were.</summary>
    [DllImport(Lib)]
    public static extern bool XCheckTypedWindowEvent(nint display, nuint window, int eventType, nint eventReturn);
}
