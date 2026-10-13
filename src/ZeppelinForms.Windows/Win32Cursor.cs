using System.Runtime.InteropServices;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Windows;

/// <summary>A native cursor from a picture, with its alpha: a 32-bit DIB with an alpha
/// channel as the color bitmap and an empty mask, as Microsoft describes for
/// alpha-blended cursors. Windows takes the color unpremultiplied.</summary>
internal static class Win32Cursor
{
    private const uint BI_BITFIELDS = 3;
    private const uint DIB_RGB_COLORS = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPV5HEADER
    {
        public uint bV5Size;
        public int bV5Width;
        public int bV5Height;
        public ushort bV5Planes;
        public ushort bV5BitCount;
        public uint bV5Compression;
        public uint bV5SizeImage;
        public int bV5XPelsPerMeter;
        public int bV5YPelsPerMeter;
        public uint bV5ClrUsed;
        public uint bV5ClrImportant;
        public uint bV5RedMask;
        public uint bV5GreenMask;
        public uint bV5BlueMask;
        public uint bV5AlphaMask;
        public uint bV5CSType;

        // CIEXYZTRIPLE: three points of three coordinates, unused here
        public int Endpoint0, Endpoint1, Endpoint2, Endpoint3, Endpoint4, Endpoint5, Endpoint6, Endpoint7, Endpoint8;

        public uint bV5GammaRed;
        public uint bV5GammaGreen;
        public uint bV5GammaBlue;
        public uint bV5Intent;
        public uint bV5ProfileData;
        public uint bV5ProfileSize;
        public uint bV5Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool fIcon;

        public int xHotspot;
        public int yHotspot;
        public nint hbmMask;
        public nint hbmColor;
    }

    [DllImport("gdi32.dll")]
    private static extern nint CreateDIBSection(nint hdc, ref BITMAPV5HEADER info, uint usage, out nint bits, nint section, uint offset);

    [DllImport("gdi32.dll")]
    private static extern nint CreateBitmap(int width, int height, uint planes, uint bitCount, nint bits);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint handle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CreateIconIndirect(ref ICONINFO info);

    /// <summary>The cursor, or 0 if Windows refused it: the caller shows the fallback.</summary>
    public static nint TryCreate(CursorImage cursor)
    {
        int width = cursor.Image.Width;
        int height = cursor.Image.Height;

        var header = new BITMAPV5HEADER
        {
            bV5Size = (uint)Marshal.SizeOf<BITMAPV5HEADER>(),
            bV5Width = width,

            // negative: the rows top to bottom, as the picture keeps them
            bV5Height = -height,
            bV5Planes = 1,
            bV5BitCount = 32,
            bV5Compression = BI_BITFIELDS,
            bV5RedMask = 0x00FF0000,
            bV5GreenMask = 0x0000FF00,
            bV5BlueMask = 0x000000FF,
            bV5AlphaMask = 0xFF000000,
        };

        nint color = CreateDIBSection(0, ref header, DIB_RGB_COLORS, out nint bits, 0, 0);
        if (color == 0 || bits == 0) return 0;

        // the alpha decides what shows: the mask is only required to exist
        nint mask = CreateBitmap(width, height, 1, 1, 0);

        try
        {
            if (mask == 0) return 0;

            var bgra = new byte[width * height * 4];

            for (int i = 0; i < width * height; i++)
            {
                (byte r, byte g, byte b, byte a) = cursor.StraightPixel(i);

                bgra[(i * 4) + 0] = b;
                bgra[(i * 4) + 1] = g;
                bgra[(i * 4) + 2] = r;
                bgra[(i * 4) + 3] = a;
            }

            Marshal.Copy(bgra, 0, bits, bgra.Length);

            var info = new ICONINFO
            {
                fIcon = false,
                xHotspot = (int)cursor.Hotspot.X,
                yHotspot = (int)cursor.Hotspot.Y,
                hbmMask = mask,
                hbmColor = color,
            };

            // the cursor copies both bitmaps: they are released below either way
            return CreateIconIndirect(ref info);
        }
        finally
        {
            DeleteObject(color);
            if (mask != 0) DeleteObject(mask);
        }
    }
}