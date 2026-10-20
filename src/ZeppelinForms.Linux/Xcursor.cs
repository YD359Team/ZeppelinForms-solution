using System.Runtime.InteropServices;

namespace ZeppelinForms.Linux;

/// <summary>libXcursor: cursors of pictures with an alpha channel. Core X has only
/// two-color bitmap cursors. Every desktop has the library — the themed system cursors
/// come from it — but it is loaded only when a picture cursor is first asked for, and
/// a system without it gets the fallback.</summary>
internal static class Xcursor
{
    internal const string Lib = "libXcursor.so.1";

    /// <summary>XcursorImage: the pixels follow the header, ARGB premultiplied.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct XcursorImage
    {
        public uint version;
        public uint size;
        public uint width;
        public uint height;
        public uint xhot;
        public uint yhot;
        public uint delay;
        public nint pixels;
    }

    [DllImport(Lib)] public static extern nint XcursorImageCreate(int width, int height);
    [DllImport(Lib)] public static extern void XcursorImageDestroy(nint image);
    [DllImport(Lib)] public static extern nuint XcursorImageLoadCursor(nint display, nint image);
}