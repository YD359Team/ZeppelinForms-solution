using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Linux;

/// <summary>The cursor over the window: a font cursor of a kind, or one made of a
/// picture through libXcursor.</summary>
internal sealed partial class X11Window
{
    private readonly Dictionary<CursorKind, nuint> _cursorCache = [];

    /// <summary>The native cursors of pictures, kept while their CursorImage lives.
    /// 0 — none could be made, and the fallback is shown.</summary>
    private readonly ConditionalWeakTable<CursorImage, StrongBox<nuint>> _imageCursors = new();

    /// <summary>The cursor defined on the window now. Compared by handle: a kind's
    /// cursor after a picture's must be defined again.</summary>
    private nuint _definedCursor;

    public void SetCursor(CursorKind cursor)
    {
        if (_window == 0) return;

        if (!_cursorCache.TryGetValue(cursor, out nuint handle))
        {
            handle = X11.XCreateFontCursor(_display, ToXShape(cursor));
            _cursorCache[cursor] = handle;
        }

        DefineCursor(handle);
    }

    public void SetCursor(CursorImage cursor)
    {
        if (_window == 0) return;

        nuint handle = _imageCursors.GetValue(cursor, c => new StrongBox<nuint>(LoadImageCursor(c))).Value;

        if (handle == 0)
        {
            SetCursor(cursor.Fallback);
            return;
        }

        DefineCursor(handle);
    }

    private void DefineCursor(nuint handle)
    {
        if (handle == _definedCursor) return;

        _definedCursor = handle;

        X11.XDefineCursor(_display, _window, handle);
        X11.XFlush(_display);
    }

    /// <summary>The picture as an Xcursor: ARGB premultiplied, which is exactly how
    /// Image keeps its pixels, only in another byte order.</summary>
    private nuint LoadImageCursor(CursorImage cursor)
    {
        try
        {
            int width = cursor.Image.Width;
            int height = cursor.Image.Height;

            nint image = Xcursor.XcursorImageCreate(width, height);
            if (image == 0) return 0;

            try
            {
                var header = Marshal.PtrToStructure<Xcursor.XcursorImage>(image);

                header.xhot = (uint)cursor.Hotspot.X;
                header.yhot = (uint)cursor.Hotspot.Y;

                Marshal.StructureToPtr(header, image, false);

                byte[] rgba = cursor.Image.Pixels;
                var argb = new int[width * height];

                for (int i = 0; i < argb.Length; i++)
                {
                    int o = i * 4;
                    argb[i] = (rgba[o + 3] << 24) | (rgba[o] << 16) | (rgba[o + 1] << 8) | rgba[o + 2];
                }

                Marshal.Copy(argb, 0, header.pixels, argb.Length);

                return Xcursor.XcursorImageLoadCursor(_display, image);
            }
            finally
            {
                Xcursor.XcursorImageDestroy(image);
            }
        }
        catch (DllNotFoundException)
        {
            // no libXcursor: the fallback is what this system can show
            return 0;
        }
    }

    // codes from X11/cursorfont.h
    private static uint ToXShape(CursorKind cursor) => cursor switch
    {
        CursorKind.Hand => 60,                      // XC_hand2
        CursorKind.IBeam => 152,                    // XC_xterm
        CursorKind.Wait => 150,                     // XC_watch
        CursorKind.SizeWestEast => 108,             // XC_sb_h_double_arrow
        CursorKind.SizeNorthSouth => 116,           // XC_sb_v_double_arrow
        CursorKind.SizeNorthWestSouthEast => 14,    // XC_bottom_right_corner
        CursorKind.SizeNorthEastSouthWest => 12,    // XC_bottom_left_corner
        CursorKind.SizeAll => 52,                   // XC_fleur
        CursorKind.Cross => 34,                     // XC_crosshair
        CursorKind.No => 88,                        // XC_pirate
        _ => 68,                                    // XC_left_ptr
    };
}