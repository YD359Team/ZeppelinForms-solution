using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ZeppelinForms.Linux;

/// <summary>Protocol errors of the X server, reported instead of ending the process.</summary>
/// <remarks>
/// <para>
/// Xlib's default handler prints the error and calls exit. A request on a window that
/// has just been destroyed — a copy through a closed window, a property of a window the
/// window manager already took away — ended the whole application, and in the tests the
/// whole test run, from inside a native call where nothing could catch it.
/// </para>
/// <para>
/// The error is written to the standard error, as Xlib writes it, and the application
/// goes on: such a request does nothing, which is what the code that sent it expected of
/// a window that is gone. A lost connection to the server is a different, I/O error and
/// still ends the process.
/// </para>
/// </remarks>
internal static unsafe class X11ErrorHandler
{
    private static int s_installed;

    public static void Install()
    {
        // one handler for the process: Xlib keeps a single one for all displays
        if (Interlocked.Exchange(ref s_installed, 1) == 1) return;

        X11.XSetErrorHandler(&OnError);
    }

    /// <summary>The XErrorEvent: int type, Display*, XID resourceid, unsigned long serial,
    /// then unsigned char error_code, request_code and minor_code. Every field before the
    /// codes takes a pointer's size, the int padded to it.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnError(nint display, nint errorEvent)
    {
        int word = IntPtr.Size;

        nuint resource = *(nuint*)(errorEvent + word * 2);
        nuint serial = *(nuint*)(errorEvent + word * 3);
        byte errorCode = *(byte*)(errorEvent + word * 4);
        byte requestCode = *(byte*)(errorEvent + word * 4 + 1);

        Console.Error.WriteLine(
            $"ZeppelinForms: X error {errorCode} of request {requestCode} on resource 0x{resource:x} (serial {serial}); the request was ignored");

        return 0;
    }
}