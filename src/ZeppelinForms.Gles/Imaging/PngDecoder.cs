using System.Buffers.Binary;
using System.IO.Compression;

namespace ZeppelinForms.Gles.Imaging;

/// <summary>
/// PNG into premultiplied RGBA: every color type and bit depth of the specification,
/// palettes with their transparency, the tRNS key color, Adam7 interlacing. The zlib
/// stream goes through the runtime's own <see cref="ZLibStream"/>.
/// </summary>
/// <remarks>Gamma, color profiles and animation chunks are ignored, as most decoders of UI
/// pictures do; a 16-bit channel keeps its high byte.</remarks>
internal static class PngDecoder
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static bool IsPng(ReadOnlySpan<byte> data) => data.StartsWith(Signature);

    public static (int Width, int Height, byte[] Pixels) Decode(ReadOnlySpan<byte> data)
    {
        if (!IsPng(data)) throw new InvalidDataException("Not a PNG file.");

        int width = 0, height = 0, bitDepth = 0, colorType = 0, interlace = 0;
        byte[]? palette = null;
        byte[]? paletteAlpha = null;
        ushort[]? keyColor = null;

        using var compressed = new MemoryStream();

        int position = Signature.Length;
        bool sawHeader = false;

        while (position + 8 <= data.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(data[position..]);
            uint type = BinaryPrimitives.ReadUInt32BigEndian(data[(position + 4)..]);

            if (length < 0 || position + 12 + length > data.Length)
                throw new InvalidDataException("A PNG chunk runs past the end of the file.");

            ReadOnlySpan<byte> chunk = data.Slice(position + 8, length);
            position += 12 + length;

            switch (type)
            {
                case 0x49484452: // IHDR
                    if (length < 13) throw new InvalidDataException("The PNG header is too short.");

                    width = BinaryPrimitives.ReadInt32BigEndian(chunk);
                    height = BinaryPrimitives.ReadInt32BigEndian(chunk[4..]);
                    bitDepth = chunk[8];
                    colorType = chunk[9];
                    interlace = chunk[12];
                    sawHeader = true;
                    break;

                case 0x504C5445: // PLTE
                    palette = chunk.ToArray();
                    break;

                case 0x74524E53: // tRNS
                    if (colorType == 3)
                    {
                        paletteAlpha = chunk.ToArray();
                    }
                    else if (colorType == 0 && length >= 2)
                    {
                        keyColor = [BinaryPrimitives.ReadUInt16BigEndian(chunk)];
                    }
                    else if (colorType == 2 && length >= 6)
                    {
                        keyColor =
                        [
                            BinaryPrimitives.ReadUInt16BigEndian(chunk),
                            BinaryPrimitives.ReadUInt16BigEndian(chunk[2..]),
                            BinaryPrimitives.ReadUInt16BigEndian(chunk[4..]),
                        ];
                    }

                    break;

                case 0x49444154: // IDAT
                    compressed.Write(chunk);
                    break;

                case 0x49454E44: // IEND
                    position = data.Length;
                    break;
            }
        }

        if (!sawHeader || width <= 0 || height <= 0)
            throw new InvalidDataException("The PNG has no valid header.");

        if ((long)width * height > 1L << 28)
            throw new InvalidDataException($"The PNG is too large: {width}×{height}.");

        int channels = colorType switch
        {
            0 => 1,
            2 => 3,
            3 => 1,
            4 => 2,
            6 => 4,
            _ => throw new InvalidDataException($"Unknown PNG color type {colorType}."),
        };

        if (bitDepth is not (1 or 2 or 4 or 8 or 16))
            throw new InvalidDataException($"Unknown PNG bit depth {bitDepth}.");

        if (colorType == 3 && palette is null)
            throw new InvalidDataException("A palette PNG has no palette.");

        compressed.Position = 0;

        byte[] raw;

        using (var inflater = new ZLibStream(compressed, CompressionMode.Decompress))
        using (var output = new MemoryStream())
        {
            inflater.CopyTo(output);
            raw = output.ToArray();
        }

        int bitsPerPixel = channels * bitDepth;
        int bytesPerPixel = Math.Max(1, bitsPerPixel / 8);

        byte[] pixels = new byte[width * height * 4];
        var format = new Format(width, colorType, bitDepth, channels, palette, paletteAlpha, keyColor);

        if (interlace == 0)
        {
            Unfilter(raw, 0, width, height, bitsPerPixel, bytesPerPixel, out _);
            Expand(raw, 0, width, height, format, pixels, 0, 0, 1, 1);
        }
        else
        {
            // Adam7: seven passes, each a smaller image of every n-th pixel
            ReadOnlySpan<(int X, int Y, int Dx, int Dy)> passes =
            [
                (0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4),
                (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2),
            ];

            int offset = 0;

            foreach ((int x0, int y0, int dx, int dy) in passes)
            {
                int passWidth = (width - x0 + dx - 1) / dx;
                int passHeight = (height - y0 + dy - 1) / dy;

                if (passWidth <= 0 || passHeight <= 0) continue;

                Unfilter(raw, offset, passWidth, passHeight, bitsPerPixel, bytesPerPixel, out int used);
                Expand(raw, offset, passWidth, passHeight, format, pixels, x0, y0, dx, dy);

                offset += used;
            }
        }

        return (width, height, pixels);
    }

    private readonly record struct Format(
        int Width, int ColorType, int BitDepth, int Channels, byte[]? Palette, byte[]? PaletteAlpha, ushort[]? KeyColor);

    /// <summary>Undo the filters row by row, in place: each row's first byte says which.</summary>
    private static void Unfilter(byte[] data, int offset, int width, int height, int bitsPerPixel, int bpp, out int used)
    {
        int stride = (width * bitsPerPixel + 7) / 8;
        used = (stride + 1) * height;

        if (offset + used > data.Length)
            throw new InvalidDataException("The PNG's pixel data is truncated.");

        for (int y = 0; y < height; y++)
        {
            int row = offset + y * (stride + 1);
            int filter = data[row];
            int current = row + 1;
            int previous = y > 0 ? current - (stride + 1) : -1;

            for (int i = 0; i < stride; i++)
            {
                int a = i >= bpp ? data[current + i - bpp] : 0;
                int b = previous >= 0 ? data[previous + i] : 0;
                int c = i >= bpp && previous >= 0 ? data[previous + i - bpp] : 0;

                int value = data[current + i];

                value += filter switch
                {
                    0 => 0,
                    1 => a,
                    2 => b,
                    3 => (a + b) / 2,
                    4 => Paeth(a, b, c),
                    _ => throw new InvalidDataException($"Unknown PNG filter {filter}."),
                };

                data[current + i] = (byte)value;
            }
        }

        static int Paeth(int a, int b, int c)
        {
            int p = a + b - c;
            int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);

            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }
    }

    /// <summary>The unfiltered rows into premultiplied RGBA, every pixel placed at
    /// (x0 + x·dx, y0 + y·dy) — the whole image, or one pass of Adam7.</summary>
    private static void Expand(
        byte[] data, int offset, int width, int height, in Format format, byte[] pixels, int x0, int y0, int dx, int dy)
    {
        int bitsPerPixel = format.Channels * format.BitDepth;
        int stride = (width * bitsPerPixel + 7) / 8;
        int depth = format.BitDepth;
        int maxSample = (1 << Math.Min(depth, 8)) - 1;

        for (int y = 0; y < height; y++)
        {
            int row = offset + y * (stride + 1) + 1;

            for (int x = 0; x < width; x++)
            {
                byte r, g, b, a = 255;

                switch (format.ColorType)
                {
                    case 0:
                        {
                            int raw = Sample(data, row, x, 0, 1, depth);
                            g = b = r = Scale(raw, depth, maxSample);

                            if (format.KeyColor is { } key && raw == key[0]) a = 0;
                            break;
                        }

                    case 2:
                        {
                            int rr = Sample(data, row, x, 0, 3, depth);
                            int gg = Sample(data, row, x, 1, 3, depth);
                            int bb = Sample(data, row, x, 2, 3, depth);

                            r = Scale(rr, depth, maxSample);
                            g = Scale(gg, depth, maxSample);
                            b = Scale(bb, depth, maxSample);

                            if (format.KeyColor is { Length: 3 } key && rr == key[0] && gg == key[1] && bb == key[2]) a = 0;
                            break;
                        }

                    case 3:
                        {
                            int index = Sample(data, row, x, 0, 1, depth);
                            byte[] palette = format.Palette!;

                            if (index * 3 + 2 < palette.Length)
                            {
                                r = palette[index * 3];
                                g = palette[index * 3 + 1];
                                b = palette[index * 3 + 2];
                            }
                            else
                            {
                                r = g = b = 0;
                            }

                            if (format.PaletteAlpha is { } alpha && index < alpha.Length) a = alpha[index];
                            break;
                        }

                    case 4:
                        g = b = r = Scale(Sample(data, row, x, 0, 2, depth), depth, maxSample);
                        a = Scale(Sample(data, row, x, 1, 2, depth), depth, maxSample);
                        break;

                    default:
                        r = Scale(Sample(data, row, x, 0, 4, depth), depth, maxSample);
                        g = Scale(Sample(data, row, x, 1, 4, depth), depth, maxSample);
                        b = Scale(Sample(data, row, x, 2, 4, depth), depth, maxSample);
                        a = Scale(Sample(data, row, x, 3, 4, depth), depth, maxSample);
                        break;
                }

                int target = ((y0 + y * dy) * format.Width + x0 + x * dx) * 4;

                pixels[target] = (byte)((r * a + 127) / 255);
                pixels[target + 1] = (byte)((g * a + 127) / 255);
                pixels[target + 2] = (byte)((b * a + 127) / 255);
                pixels[target + 3] = a;
            }
        }
    }

    /// <summary>A channel of a pixel as stored: packed bits below 8, a big-endian word at 16.</summary>
    private static int Sample(byte[] data, int row, int x, int channel, int channels, int depth)
    {
        switch (depth)
        {
            case 8:
                return data[row + x * channels + channel];

            case 16:
                {
                    int i = row + (x * channels + channel) * 2;
                    return (data[i] << 8) | data[i + 1];
                }

            default:
                {
                    int bit = (x * channels + channel) * depth;
                    int value = data[row + (bit >> 3)];
                    int shift = 8 - depth - (bit & 7);
                    return (value >> shift) & ((1 << depth) - 1);
                }
        }
    }

    private static byte Scale(int sample, int depth, int maxSample) => depth switch
    {
        8 => (byte)sample,
        16 => (byte)(sample >> 8),
        _ => (byte)(sample * 255 / maxSample),
    };
}