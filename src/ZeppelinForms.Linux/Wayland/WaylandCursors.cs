using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Linux.Wayland;

/// <summary>A cursor picture ready to attach: a buffer, its hotspot in buffer pixels and
/// the scale it was made for.</summary>
internal readonly record struct WaylandCursorImage(WlBuffer Buffer, int Width, int Height, int HotspotX, int HotspotY, int Scale);

/// <summary>
/// The cursors of the user's Xcursor theme through libwayland-cursor, and cursors made of
/// pictures.
/// </summary>
/// <remarks>
/// <para>
/// A Wayland client draws its own cursor: it attaches a picture to a surface of its own and
/// hands the surface to the pointer. The theme is XCURSOR_THEME at XCURSOR_SIZE — what
/// desktops set for their applications, and what GTK and Qt read as well; one theme per
/// scale, loaded on the first window of that scale.
/// </para>
/// <para>
/// An animated cursor — a theme's wait cursor — shows its first frame.
/// </para>
/// </remarks>
internal sealed unsafe class WaylandCursors : IDisposable
{
    internal const string Lib = "libwayland-cursor.so.0";

    [StructLayout(LayoutKind.Sequential)]
    private struct WlCursorImage
    {
        public uint Width;
        public uint Height;
        public uint HotspotX;
        public uint HotspotY;
        public uint Delay;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WlCursor
    {
        public uint ImageCount;
        public WlCursorImage** Images;
        public nint Name;
    }

    [DllImport(Lib)] private static extern nint wl_cursor_theme_load([MarshalAs(UnmanagedType.LPStr)] string? name, int size, nint shm);
    [DllImport(Lib)] private static extern void wl_cursor_theme_destroy(nint theme);
    [DllImport(Lib)] private static extern WlCursor* wl_cursor_theme_get_cursor(nint theme, [MarshalAs(UnmanagedType.LPStr)] string name);
    [DllImport(Lib)] private static extern nint wl_cursor_image_get_buffer(WlCursorImage* image);

    private readonly WlShm _shm;
    private readonly Dictionary<int, nint> _themes = [];
    private readonly Dictionary<(CursorKind, int), WaylandCursorImage?> _kinds = [];

    /// <summary>The buffers of pictures, made once per picture while it lives.</summary>
    private readonly ConditionalWeakTable<CursorImage, PictureCursor> _pictures = new();
    private readonly List<PictureCursor> _pictureList = [];

    private readonly bool _available = NativeProbe.Has(Lib);

    public WaylandCursors(WlShm shm) => _shm = shm;

    /// <summary>The theme's cursor of a kind at a scale; null where the theme has none of
    /// its names, or there is no libwayland-cursor.</summary>
    public WaylandCursorImage? Get(CursorKind kind, int scale)
    {
        if (_kinds.TryGetValue((kind, scale), out WaylandCursorImage? known)) return known;

        WaylandCursorImage? found = null;

        if (Theme(scale) is var theme and not 0)
        {
            foreach (string name in Names(kind))
            {
                WlCursor* cursor = wl_cursor_theme_get_cursor(theme, name);
                if (cursor == null || cursor->ImageCount == 0) continue;

                WlCursorImage* image = cursor->Images[0];
                nint buffer = wl_cursor_image_get_buffer(image);
                if (buffer == 0) continue;

                // the theme's buffer, not ours: it lives and dies with the theme
                found = new WaylandCursorImage(
                    WlBuffer.Foreign(buffer), (int)image->Width, (int)image->Height,
                    (int)image->HotspotX, (int)image->HotspotY, scale);

                break;
            }
        }

        _kinds[(kind, scale)] = found;

        return found;
    }

    /// <summary>A picture as a cursor, at scale 1: it is the size it was drawn at.</summary>
    public WaylandCursorImage Get(CursorImage cursor) =>
        _pictures.GetValue(cursor, c =>
        {
            var picture = new PictureCursor(_shm, c);
            _pictureList.Add(picture);
            return picture;
        }).Image;

    private nint Theme(int scale)
    {
        if (!_available) return 0;

        if (_themes.TryGetValue(scale, out nint theme)) return theme;

        string? name = Environment.GetEnvironmentVariable("XCURSOR_THEME") is { Length: > 0 } set ? set : null;

        int size = int.TryParse(Environment.GetEnvironmentVariable("XCURSOR_SIZE"), out int parsed) && parsed > 0
            ? parsed
            : 24;

        // null — the theme "default", which is what the desktop links to its own
        theme = wl_cursor_theme_load(name, size * scale, _shm.Handle);
        _themes[scale] = theme;

        return theme;
    }

    /// <summary>The names to look for, the freedesktop (CSS) one first, then the old X11
    /// one that older themes have.</summary>
    private static string[] Names(CursorKind kind) => kind switch
    {
        CursorKind.Hand => ["pointer", "hand2", "hand1"],
        CursorKind.IBeam => ["text", "xterm"],
        CursorKind.Wait => ["wait", "watch"],
        CursorKind.SizeWestEast => ["ew-resize", "sb_h_double_arrow", "h_double_arrow"],
        CursorKind.SizeNorthSouth => ["ns-resize", "sb_v_double_arrow", "v_double_arrow"],
        CursorKind.SizeNorthWestSouthEast => ["nwse-resize", "bottom_right_corner", "size_fdiag"],
        CursorKind.SizeNorthEastSouthWest => ["nesw-resize", "bottom_left_corner", "size_bdiag"],
        CursorKind.SizeAll => ["move", "fleur", "all-scroll"],
        CursorKind.Cross => ["crosshair", "cross"],
        CursorKind.No => ["not-allowed", "crossed_circle", "circle"],
        _ => ["default", "left_ptr"],
    };

    public void Dispose()
    {
        foreach (PictureCursor picture in _pictureList)
            picture.Dispose();

        _pictureList.Clear();
        _kinds.Clear();

        foreach (nint theme in _themes.Values)
            if (theme != 0)
                wl_cursor_theme_destroy(theme);

        _themes.Clear();
    }

    /// <summary>A picture's pixels in a wl_shm buffer of its own.</summary>
    private sealed class PictureCursor : IDisposable
    {
        private readonly SharedMemory _memory;
        private readonly WlShmPool _pool;

        public PictureCursor(WlShm shm, CursorImage cursor)
        {
            int width = cursor.Image.Width;
            int height = cursor.Image.Height;
            int stride = width * 4;

            _memory = SharedMemory.Create(stride * height);

            // RGBA premultiplied into ARGB8888's B, G, R, A in memory
            byte[] rgba = cursor.Image.Pixels;
            var pixels = new Span<byte>((void*)_memory.Pointer, stride * height);

            for (int i = 0; i < width * height; i++)
            {
                int o = i * 4;

                pixels[o] = rgba[o + 2];
                pixels[o + 1] = rgba[o + 1];
                pixels[o + 2] = rgba[o];
                pixels[o + 3] = rgba[o + 3];
            }

            _pool = shm.CreatePool(_memory.FileDescriptor, _memory.Size);

            WlBuffer buffer = _pool.CreateBuffer(0, width, height, stride, (uint)WlShm.FormatKind.Argb8888);

            Image = new WaylandCursorImage(buffer, width, height, (int)cursor.Hotspot.X, (int)cursor.Hotspot.Y, 1);
        }

        public WaylandCursorImage Image { get; }

        public void Dispose()
        {
            Image.Buffer.Destroy();
            _pool.Destroy();
            _memory.Dispose();
        }
    }
}