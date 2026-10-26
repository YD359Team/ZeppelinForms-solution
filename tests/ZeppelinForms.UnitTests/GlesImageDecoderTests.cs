using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Xunit;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Gles.Imaging;

namespace ZeppelinForms.UnitTests;

/// <summary>The managed decoders of the GLES backend: PNG of every kind a UI ships, JPEG
/// baseline and progressive, the first frame of a GIF, BMP, and the icons inside the
/// framework's own ICO.</summary>
public class GlesImageDecoderTests
{
    #region A small PNG writer

    private static byte[] Png(int width, int height, int bitDepth, int colorType, Func<int, byte[]> row,
        byte[]? palette = null, byte[]? transparency = null, int filter = 0, bool interlaced = false)
    {
        int channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, _ => 4 };
        int bpp = Math.Max(1, channels * bitDepth / 8);

        using var raw = new MemoryStream();

        if (!interlaced)
        {
            WriteRows(raw, height, y => row(y), bpp, filter);
        }
        else
        {
            // each Adam7 pass is the image's every n-th pixel; the rows given are whole,
            // 8-bit samples only, which is all the tests interlace
            (int X, int Y, int Dx, int Dy)[] passes =
                [(0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)];

            foreach ((int x0, int y0, int dx, int dy) in passes)
            {
                int pw = (width - x0 + dx - 1) / dx;
                int ph = (height - y0 + dy - 1) / dy;
                if (pw <= 0 || ph <= 0) continue;

                WriteRows(raw, ph, py =>
                {
                    byte[] full = row(y0 + py * dy);
                    byte[] pass = new byte[pw * bpp];

                    for (int px = 0; px < pw; px++)
                        Array.Copy(full, (x0 + px * dx) * bpp, pass, px * bpp, bpp);

                    return pass;
                }, bpp, filter);
            }
        }

        using var file = new MemoryStream();
        file.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = (byte)bitDepth;
        header[9] = (byte)colorType;
        header[12] = (byte)(interlaced ? 1 : 0);
        Chunk(file, "IHDR", header);

        if (palette is not null) Chunk(file, "PLTE", palette);
        if (transparency is not null) Chunk(file, "tRNS", transparency);

        using (var compressed = new MemoryStream())
        {
            using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
                z.Write(raw.ToArray());

            // split in two: the decoder must join the IDAT chunks
            byte[] data = compressed.ToArray();
            Chunk(file, "IDAT", data[..(data.Length / 2)]);
            Chunk(file, "IDAT", data[(data.Length / 2)..]);
        }

        Chunk(file, "IEND", []);

        return file.ToArray();
    }

    private static void WriteRows(Stream raw, int height, Func<int, byte[]> row, int bpp, int filter)
    {
        byte[]? previous = null;

        for (int y = 0; y < height; y++)
        {
            byte[] current = row(y);
            byte[] filtered = new byte[current.Length];

            for (int i = 0; i < current.Length; i++)
            {
                int a = i >= bpp ? current[i - bpp] : 0;
                int b = previous is not null ? previous[i] : 0;
                int c = i >= bpp && previous is not null ? previous[i - bpp] : 0;

                int predictor = filter switch
                {
                    1 => a,
                    2 => b,
                    3 => (a + b) / 2,
                    4 => Paeth(a, b, c),
                    _ => 0,
                };

                filtered[i] = (byte)(current[i] - predictor);
            }

            raw.WriteByte((byte)filter);
            raw.Write(filtered);
            previous = current;
        }

        static int Paeth(int a, int b, int c)
        {
            int p = a + b - c;
            int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }
    }

    private static void Chunk(Stream file, string type, byte[] data)
    {
        byte[] length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        file.Write(length);
        file.Write(Encoding.ASCII.GetBytes(type));
        file.Write(data);

        // the CRC is not checked by the decoder: any value does
        file.Write(new byte[4]);
    }

    private static Image Decode(byte[] data) => new GlesImageDecoder().Decode(new MemoryStream(data));

    private static (byte R, byte G, byte B, byte A) Pixel(Image image, int x, int y)
    {
        int i = (y * image.Width + x) * 4;
        return (image.Pixels[i], image.Pixels[i + 1], image.Pixels[i + 2], image.Pixels[i + 3]);
    }

    #endregion

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void EveryFilterUnfiltersToThePixels(int filter)
    {
        byte[] Row(int y)
        {
            byte[] row = new byte[7 * 4];
            for (int x = 0; x < 7; x++)
            {
                row[x * 4] = (byte)(x * 30 + y);
                row[x * 4 + 1] = (byte)(y * 40);
                row[x * 4 + 2] = (byte)(x * y * 7);
                row[x * 4 + 3] = 255;
            }
            return row;
        }

        Image image = Decode(Png(7, 5, 8, 6, Row, filter: filter));

        Assert.Equal(7, image.Width);
        Assert.Equal(5, image.Height);

        for (int y = 0; y < 5; y++)
            for (int x = 0; x < 7; x++)
                Assert.Equal(((byte)(x * 30 + y), (byte)(y * 40), (byte)(x * y * 7), (byte)255), Pixel(image, x, y));
    }

    [Fact]
    public void AlphaIsPremultiplied()
    {
        Image image = Decode(Png(1, 1, 8, 6, _ => [200, 100, 50, 128]));

        Assert.Equal(((byte)100, (byte)50, (byte)25, (byte)128), Pixel(image, 0, 0));
    }

    [Fact]
    public void APaletteWithTransparencyGivesItsColors()
    {
        byte[] palette = [255, 0, 0, 0, 255, 0, 0, 0, 255];
        byte[] alpha = [255, 0];

        // 2 bits a pixel: indices 0 1 2 1 packed into one byte
        Image image = Decode(Png(4, 1, 2, 3, _ => [0b00_01_10_01], palette, alpha));

        Assert.Equal(((byte)255, (byte)0, (byte)0, (byte)255), Pixel(image, 0, 0));
        Assert.Equal(((byte)0, (byte)0, (byte)0, (byte)0), Pixel(image, 1, 0));
        Assert.Equal(((byte)0, (byte)0, (byte)255, (byte)255), Pixel(image, 2, 0));
    }

    [Fact]
    public void OneBitGrayIsBlackAndWhite()
    {
        Image image = Decode(Png(10, 1, 1, 0, _ => [0b1010_0000, 0b1100_0000]));

        Assert.Equal((byte)255, Pixel(image, 0, 0).R);
        Assert.Equal((byte)0, Pixel(image, 1, 0).R);
        Assert.Equal((byte)255, Pixel(image, 8, 0).R);
        Assert.Equal((byte)255, Pixel(image, 9, 0).R);
    }

    [Fact]
    public void SixteenBitsKeepTheirHighByte()
    {
        Image image = Decode(Png(1, 1, 16, 2, _ => [0x12, 0x34, 0xAB, 0xCD, 0xFF, 0x00]));

        Assert.Equal(((byte)0x12, (byte)0xAB, (byte)0xFF, (byte)255), Pixel(image, 0, 0));
    }

    [Fact]
    public void AKeyColorIsTransparent()
    {
        Image image = Decode(Png(2, 1, 8, 2, _ => [10, 20, 30, 40, 50, 60], transparency: [0, 10, 0, 20, 0, 30]));

        Assert.Equal((byte)0, Pixel(image, 0, 0).A);
        Assert.Equal((byte)255, Pixel(image, 1, 0).A);
    }

    [Fact]
    public void InterlacedPassesLandWhereTheyBelong()
    {
        byte[] Row(int y)
        {
            byte[] row = new byte[11 * 3];
            for (int x = 0; x < 11; x++)
            {
                row[x * 3] = (byte)(x * 20);
                row[x * 3 + 1] = (byte)(y * 20);
                row[x * 3 + 2] = 7;
            }
            return row;
        }

        Image image = Decode(Png(11, 9, 8, 2, Row, filter: 4, interlaced: true));

        for (int y = 0; y < 9; y++)
            for (int x = 0; x < 11; x++)
                Assert.Equal(((byte)(x * 20), (byte)(y * 20), (byte)7, (byte)255), Pixel(image, x, y));
    }

    [Fact]
    public void ABottomUpBmpIsTurnedRightWayUp()
    {
        // 2×2, 24 bits, rows padded to 4 bytes, the bottom row first
        byte[] pixels =
        [
            0, 0, 255, 0, 255, 0, 0, 0,     // bottom: red, green
            255, 0, 0, 255, 255, 255, 0, 0, // top: blue, white
        ];

        byte[] bmp = Bmp(2, 2, 24, pixels);
        Image image = Decode(bmp);

        Assert.Equal(((byte)0, (byte)0, (byte)255, (byte)255), Pixel(image, 0, 0));
        Assert.Equal(((byte)255, (byte)255, (byte)255, (byte)255), Pixel(image, 1, 0));
        Assert.Equal(((byte)255, (byte)0, (byte)0, (byte)255), Pixel(image, 0, 1));
        Assert.Equal(((byte)0, (byte)255, (byte)0, (byte)255), Pixel(image, 1, 1));
    }

    [Fact]
    public void A32BitBmpUsesItsAlphaOnlyWhenItHasSome()
    {
        Image opaque = Decode(Bmp(1, 1, 32, [10, 20, 30, 0]));
        Assert.Equal(((byte)30, (byte)20, (byte)10, (byte)255), Pixel(opaque, 0, 0));

        Image translucent = Decode(Bmp(1, 1, 32, [100, 100, 100, 51]));
        Assert.Equal(((byte)20, (byte)20, (byte)20, (byte)51), Pixel(translucent, 0, 0));
    }

    [Fact]
    public void TheFrameworksOwnIconDecodes()
    {
        // through the decoder itself, not the shared Current: tests run in parallel
        Image image = Decode(Assets.Logo.GetImageFile(32, 32));

        Assert.True(image.Width > 0 && image.Height > 0);
        Assert.True(image.Pixels.Any(b => b != 0), "the icon decoded to nothing");
    }

    #region JPEG and GIF

    // 16 × 16, red on the left half, blue on the right, 4:2:0, quality 95 — written by
    // libjpeg through Pillow; libjpeg-turbo decodes them to the values asserted
    private const string RedBlueBaseline =
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAIBAQEBAQIBAQECAgICAgQDAgICAgUEBAMEBgUGBgYFBgYGBwkIBgcJBwYGCAsICQoKCgoKBggLDAsKDAkKCgr/2wBDAQICAgICAgUDAwUKBwYHCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgr/wAARCAAQABADASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD4vr5Dr68r5Dr+qP2Zf/NV/wDcj/7uH9qftDP+aZ/7nf8A3UP/2Q==";

    private const string RedBlueProgressive =
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAIBAQEBAQIBAQECAgICAgQDAgICAgUEBAMEBgUGBgYFBgYGBwkIBgcJBwYGCAsICQoKCgoKBggLDAsKDAkKCgr/2wBDAQICAgICAgUDAwUKBwYHCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgr/wgARCAAQABADASIAAhEBAxEB/8QAFAABAAAAAAAAAAAAAAAAAAAAB//EABQBAQAAAAAAAAAAAAAAAAAAAAf/2gAMAwEAAhADEAAAARchXiFUav/EABQQAQAAAAAAAAAAAAAAAAAAACD/2gAIAQEAAQUCH//EABgRAAIDAAAAAAAAAAAAAAAAAAAIRYPD/9oACAEDAQE/AWGjLsj/xAAYEQACAwAAAAAAAAAAAAAAAAAACEWDw//aAAgBAgEBPwFZZWjY/8QAFBABAAAAAAAAAAAAAAAAAAAAIP/aAAgBAQAGPwIf/8QAFBABAAAAAAAAAAAAAAAAAAAAIP/aAAgBAQABPyEf/9oADAMBAAIAAwAAABAD/8QAFBEBAAAAAAAAAAAAAAAAAAAAAP/aAAgBAwEBPxAf/8QAFBEBAAAAAAAAAAAAAAAAAAAAAP/aAAgBAgEBPxAn/8QAFBABAAAAAAAAAAAAAAAAAAAAIP/aAAgBAQABPxAf/9k=";

    // the same, 4:4:4, a restart marker after every block
    private const string RedBlueRestarts =
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAIBAQEBAQIBAQECAgICAgQDAgICAgUEBAMEBgUGBgYFBgYGBwkIBgcJBwYGCAsICQoKCgoKBggLDAsKDAkKCgr/2wBDAQICAgICAgUDAwUKBwYHCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgr/wAARCAAQABADAREAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/90ABAAB/9oADAMBAAIRAxEAPwD4vr+Uz/fw/9D8c6/38Pys/9H4vr+Uz/fw/9L8c6/38Pys/9k=";

    // 8 × 8 grayscale of 200
    private const string Gray200 =
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAIBAQEBAQIBAQECAgICAgQDAgICAgUEBAMEBgUGBgYFBgYGBwkIBgcJBwYGCAsICQoKCgoKBggLDAsKDAkKCgr/wAALCAAIAAgBAREA/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/9oACAEBAAA/AP0gr//Z";

    // 64 × 48 of (40, 160, 80), quality 90
    private const string Green64x48 =
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCAAwAEADASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwDnKKKK+KP5yCiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooA/9k=";

    // 4 × 3, interlaced, palette red, green, blue and a transparent index 3: the pixel
    // at (x, y) is index (x + y) mod 4
    private const string PaletteGif = "R0lGODlhBAADAIEAAP8AAAD/AAAA/wkJCSH5BAEAAAMALAAAAAAEAAMAAAgNAAEEEDBg4AAABAUGBAA7";

    [Theory]
    [InlineData(RedBlueBaseline)]
    [InlineData(RedBlueProgressive)]
    [InlineData(RedBlueRestarts)]
    public void AJpegDecodesAsLibjpegDecodesIt(string base64)
    {
        Image image = Decode(Convert.FromBase64String(base64));

        Assert.Equal(16, image.Width);
        Assert.Equal(16, image.Height);

        Assert.Equal(((byte)254, (byte)0, (byte)0, (byte)255), Pixel(image, 1, 8));
        Assert.Equal(((byte)0, (byte)0, (byte)254, (byte)255), Pixel(image, 14, 8));
        Assert.Equal(((byte)254, (byte)0, (byte)0, (byte)255), Pixel(image, 0, 0));
        Assert.Equal(((byte)0, (byte)0, (byte)254, (byte)255), Pixel(image, 15, 15));
    }

    [Fact]
    public void AGrayJpegIsGray()
    {
        Image image = Decode(Convert.FromBase64String(Gray200));

        Assert.Equal(((byte)200, (byte)200, (byte)200, (byte)255), Pixel(image, 3, 3));
    }

    [Fact]
    public void ALargeJpegIsDecodedScaledDown()
    {
        // a quarter of the size is still no smaller than the 16 wanted: decoded at it
        Image image = new GlesImageDecoder { MaxDimension = 16 }.Decode(new MemoryStream(Convert.FromBase64String(Green64x48)));

        Assert.Equal(16, image.Width);
        Assert.Equal(12, image.Height);

        (byte r, byte g, byte b, byte a) = Pixel(image, 8, 6);

        Assert.InRange(r, 38, 43);
        Assert.InRange(g, 157, 163);
        Assert.InRange(b, 77, 83);
        Assert.Equal(255, a);
    }

    [Fact]
    public void ACutJpegDecodesWhatItHas()
    {
        byte[] whole = Convert.FromBase64String(RedBlueBaseline);
        byte[] cut = whole[..(whole.Length - 20)];

        Image image = Decode(cut);

        Assert.Equal(16, image.Width);
    }

    [Fact]
    public void AnArithmeticJpegIsRefusedByName()
    {
        // SOI, then a frame header of the arithmetic-coded process
        byte[] data = [0xFF, 0xD8, 0xFF, 0xC9, 0, 11, 8, 0, 1, 0, 1, 1, 1, 0x11, 0];

        var error = Assert.Throws<InvalidDataException>(() => Decode(data));

        Assert.Contains("Arithmetic", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AGifGivesItsFirstFrameWithItsTransparency()
    {
        Image image = Decode(Convert.FromBase64String(PaletteGif));

        Assert.Equal(4, image.Width);
        Assert.Equal(3, image.Height);

        (byte, byte, byte, byte)[] colors =
            [(255, 0, 0, 255), (0, 255, 0, 255), (0, 0, 255, 255), (0, 0, 0, 0)];

        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 4; x++)
                Assert.Equal(colors[(x + y) % 4], Pixel(image, x, y));
        }
    }

    [Fact]
    public void AWebPIsRefusedByName()
    {
        byte[] data = [.. "RIFF"u8, 0, 0, 0, 0, .. "WEBPVP8 "u8];

        var error = Assert.Throws<InvalidDataException>(() => Decode(data));

        Assert.Contains("WebP", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APictureLargerThanTheLimitIsAveragedDown()
    {
        // 4 × 2 of white and black columns: halved, every pixel the average grey
        byte[] row = [255, 255, 255, 0, 0, 0, 0, 0, 255, 255, 255, 0, 0, 0, 0, 0];
        byte[] bmp = Bmp(4, 2, 32, [.. row, .. row]);

        Image image = new GlesImageDecoder { MaxDimension = 2 }.Decode(new MemoryStream(bmp));

        Assert.Equal(2, image.Width);
        Assert.Equal(1, image.Height);
        Assert.Equal(((byte)128, (byte)128, (byte)128, (byte)255), Pixel(image, 0, 0));
    }

    #endregion

    private static byte[] Bmp(int width, int height, int bitCount, byte[] pixels)
    {
        byte[] file = new byte[14 + 40 + pixels.Length];

        file[0] = (byte)'B';
        file[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(2), file.Length);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(22), height);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(26), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(28), (ushort)bitCount);
        pixels.CopyTo(file, 54);

        return file;
    }
}