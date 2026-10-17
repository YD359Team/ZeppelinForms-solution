using System.Runtime.CompilerServices;
using Android.Graphics;
using Android.Views;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Android;

/// <summary>The mouse pointer — a mouse on a tablet, DeX, a Chromebook: the system's
/// icons of the kinds, and icons made of pictures. Until 0.16 the pointer stayed the
/// arrow whatever the element asked for.</summary>
public sealed partial class AndroidPlatform
{
    /// <summary>The icons of pictures, kept while their CursorImage lives.</summary>
    private readonly ConditionalWeakTable<CursorImage, PointerIcon> _imageIcons = new();

    internal void SetCursor(CursorKind cursor)
    {
        if (_view is null) return;

        _view.PointerIcon = PointerIcon.GetSystemIcon(_activity, ToIconType(cursor));
    }

    internal void SetCursor(CursorImage cursor)
    {
        if (_view is null) return;

        _view.PointerIcon = _imageIcons.GetValue(cursor, CreateIcon);
    }

    /// <summary>Android takes the colors unpremultiplied, as ARGB ints.</summary>
    private PointerIcon CreateIcon(CursorImage cursor)
    {
        int width = cursor.Image.Width;
        int height = cursor.Image.Height;
        var colors = new int[width * height];

        for (int i = 0; i < colors.Length; i++)
        {
            (byte r, byte g, byte b, byte a) = cursor.StraightPixel(i);
            colors[i] = (a << 24) | (r << 16) | (g << 8) | b;
        }

        Bitmap? bitmap = Bitmap.CreateBitmap(colors, width, height, Bitmap.Config.Argb8888!);

        return bitmap is null
            ? PointerIcon.GetSystemIcon(_activity, ToIconType(cursor.Fallback))
            : PointerIcon.Create(bitmap, cursor.Hotspot.X, cursor.Hotspot.Y);
    }

    private static PointerIconType ToIconType(CursorKind cursor) => cursor switch
    {
        CursorKind.Hand => PointerIconType.Hand,
        CursorKind.IBeam => PointerIconType.Text,
        CursorKind.Wait => PointerIconType.Wait,
        CursorKind.SizeWestEast => PointerIconType.HorizontalDoubleArrow,
        CursorKind.SizeNorthSouth => PointerIconType.VerticalDoubleArrow,
        CursorKind.SizeNorthWestSouthEast => PointerIconType.TopLeftDiagonalDoubleArrow,
        CursorKind.SizeNorthEastSouthWest => PointerIconType.TopRightDiagonalDoubleArrow,
        CursorKind.SizeAll => PointerIconType.AllScroll,
        CursorKind.Cross => PointerIconType.Crosshair,
        CursorKind.No => PointerIconType.NoDrop,
        _ => PointerIconType.Arrow,
    };
}