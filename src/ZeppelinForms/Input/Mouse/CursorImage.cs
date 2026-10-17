using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Input.Mouse;

/// <summary>A cursor drawn from a picture: a zoom lens, a rotation arrow, an editor's
/// own brush.</summary>
/// <remarks>
/// <para>
/// The picture is shown at its own pixel size, its <see cref="Hotspot"/> — the point
/// that clicks — in its pixels from the top-left corner. 32×32 is the size every
/// platform takes; the browser shows nothing larger than 128.
/// </para>
/// <para>
/// A platform makes it into a native cursor the first time it is shown and keeps that
/// for as long as this object lives: make one per cursor and reuse it, rather than a
/// new one per move.
/// </para>
/// </remarks>
public sealed class CursorImage
{
    /// <param name="fallback">What is shown where a platform can't make a cursor of the
    /// picture — X11 without libXcursor, a failure of the system.</param>
    public CursorImage(Image image, Point hotspot, CursorKind fallback = CursorKind.Arrow)
    {
        ArgumentNullException.ThrowIfNull(image);

        Image = image;

        // whole pixels inside the picture: a hotspot outside it is refused by Windows
        // and moved by X11, each in its own way
        Hotspot = new Point(
            Math.Clamp(MathF.Round(hotspot.X), 0, image.Width - 1),
            Math.Clamp(MathF.Round(hotspot.Y), 0, image.Height - 1));

        // Default means "inherited" on an element; as a cursor's own it means the arrow
        Fallback = fallback == CursorKind.Default ? CursorKind.Arrow : fallback;
    }

    public Image Image { get; }

    /// <summary>The point that clicks, in the picture's pixels.</summary>
    public Point Hotspot { get; }

    public CursorKind Fallback { get; }

    /// <summary>A pixel with its color not multiplied by its alpha, as Windows' and
    /// Android's cursors take it. Image keeps premultiplied pixels.</summary>
    internal (byte R, byte G, byte B, byte A) StraightPixel(int index)
    {
        byte[] pixels = Image.Pixels;
        int offset = index * 4;
        byte a = pixels[offset + 3];

        if (a == 0) return (0, 0, 0, 0);
        if (a == 255) return (pixels[offset], pixels[offset + 1], pixels[offset + 2], 255);

        return (Straight(pixels[offset], a), Straight(pixels[offset + 1], a), Straight(pixels[offset + 2], a), a);

        static byte Straight(byte channel, byte alpha) =>
            (byte)Math.Min(255, ((channel * 255) + (alpha / 2)) / alpha);
    }
}