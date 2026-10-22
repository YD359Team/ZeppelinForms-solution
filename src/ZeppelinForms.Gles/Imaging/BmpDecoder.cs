using System.Buffers.Binary;
using System.Numerics;

namespace ZeppelinForms.Gles.Imaging;

/// <summary>
/// BMP into premultiplied RGBA: 1-, 4- and 8-bit palettes, 16-, 24- and 32-bit pixels,
/// bit fields, rows bottom-up or top-down — what Windows icons and old resources hold.
/// </summary>
/// <remarks>A 32-bit picture without bit fields has its alpha in the fourth byte only by
/// custom — icons do, screenshots leave it zero. It is used when any pixel has some;
/// a picture all zeros there is opaque.</remarks>
internal static class BmpDecoder
{
    private const int BI_RGB = 0;
    private const int BI_BITFIELDS = 3;
    private const int BI_ALPHABITFIELDS = 6;

    public static bool IsBmp(ReadOnlySpan<byte> data) => data.Length >= 26 && data[0] == 'B' && data[1] == 'M';

    public static (int Width, int Height, byte[] Pixels) Decode(ReadOnlySpan<byte> data)
    {
        if (!IsBmp(data)) throw new InvalidDataException("Not a BMP file.");

        int pixelOffset = BinaryPrimitives.ReadInt32LittleEndian(data[10..]);
        ReadOnlySpan<byte> header = data[14..];

        int headerSize = BinaryPrimitives.ReadInt32LittleEndian(header);

        int width, height, bitCount, compression = BI_RGB, colorsUsed = 0;

        if (headerSize == 12)
        {
            // the OS/2 header of the oldest files
            width = BinaryPrimitives.ReadInt16LittleEndian(header[4..]);
            height = BinaryPrimitives.ReadInt16LittleEndian(header[6..]);
            bitCount = BinaryPrimitives.ReadUInt16LittleEndian(header[10..]);
        }
        else
        {
            if (header.Length < 40) throw new InvalidDataException("The BMP header is too short.");

            width = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
            height = BinaryPrimitives.ReadInt32LittleEndian(header[8..]);
            bitCount = BinaryPrimitives.ReadUInt16LittleEndian(header[14..]);
            compression = BinaryPrimitives.ReadInt32LittleEndian(header[16..]);
            colorsUsed = BinaryPrimitives.ReadInt32LittleEndian(header[32..]);
        }

        bool topDown = height < 0;
        height = Math.Abs(height);

        if (width <= 0 || height <= 0 || (long)width * height > 1L << 28)
            throw new InvalidDataException($"The BMP has an invalid size: {width}×{height}.");

        if (compression is not (BI_RGB or BI_BITFIELDS or BI_ALPHABITFIELDS))
            throw new InvalidDataException("Compressed BMP files are not supported.");

        uint redMask = 0, greenMask = 0, blueMask = 0, alphaMask = 0;

        if (compression is BI_BITFIELDS or BI_ALPHABITFIELDS)
        {
            // in a V4/V5 header the masks are part of it; after a V1 header they follow it
            ReadOnlySpan<byte> masks = header[40..];

            redMask = BinaryPrimitives.ReadUInt32LittleEndian(masks);
            greenMask = BinaryPrimitives.ReadUInt32LittleEndian(masks[4..]);
            blueMask = BinaryPrimitives.ReadUInt32LittleEndian(masks[8..]);

            if (headerSize >= 56 || compression == BI_ALPHABITFIELDS)
                alphaMask = BinaryPrimitives.ReadUInt32LittleEndian(masks[12..]);
        }
        else if (bitCount == 16)
        {
            redMask = 0x7C00;
            greenMask = 0x03E0;
            blueMask = 0x001F;
        }

        // the palette right after the header — and after the masks of a V1 header
        int paletteStart = 14 + headerSize + (compression == BI_BITFIELDS && headerSize == 40 ? 12 : 0);
        int paletteEntrySize = headerSize == 12 ? 3 : 4;
        int paletteCount = bitCount <= 8 ? (colorsUsed > 0 ? colorsUsed : 1 << bitCount) : 0;

        int stride = (width * bitCount + 31) / 32 * 4;

        if (pixelOffset + (long)stride * height > data.Length)
            throw new InvalidDataException("The BMP's pixel data is truncated.");

        byte[] pixels = new byte[width * height * 4];
        bool anyAlpha = false;

        for (int y = 0; y < height; y++)
        {
            int sourceRow = pixelOffset + (topDown ? y : height - 1 - y) * stride;
            ReadOnlySpan<byte> row = data.Slice(sourceRow, stride);

            for (int x = 0; x < width; x++)
            {
                byte r, g, b, a = 255;

                switch (bitCount)
                {
                    case 1:
                    case 2:
                    case 4:
                    case 8:
                        {
                            int bit = x * bitCount;
                            int index = (row[bit >> 3] >> (8 - bitCount - (bit & 7))) & ((1 << bitCount) - 1);

                            if (index >= paletteCount) index = 0;

                            int entry = paletteStart + index * paletteEntrySize;

                            if (entry + 2 < data.Length)
                            {
                                b = data[entry];
                                g = data[entry + 1];
                                r = data[entry + 2];
                            }
                            else
                            {
                                r = g = b = 0;
                            }

                            break;
                        }

                    case 16:
                        {
                            uint value = BinaryPrimitives.ReadUInt16LittleEndian(row[(x * 2)..]);
                            r = Channel(value, redMask);
                            g = Channel(value, greenMask);
                            b = Channel(value, blueMask);
                            if (alphaMask != 0) a = Channel(value, alphaMask);
                            break;
                        }

                    case 24:
                        b = row[x * 3];
                        g = row[x * 3 + 1];
                        r = row[x * 3 + 2];
                        break;

                    case 32:
                        {
                            if (compression == BI_RGB)
                            {
                                b = row[x * 4];
                                g = row[x * 4 + 1];
                                r = row[x * 4 + 2];
                                a = row[x * 4 + 3];
                            }
                            else
                            {
                                uint value = BinaryPrimitives.ReadUInt32LittleEndian(row[(x * 4)..]);
                                r = Channel(value, redMask);
                                g = Channel(value, greenMask);
                                b = Channel(value, blueMask);
                                a = alphaMask != 0 ? Channel(value, alphaMask) : (byte)255;
                            }

                            if (a != 0) anyAlpha = true;
                            break;
                        }

                    default:
                        throw new InvalidDataException($"BMP files of {bitCount} bits per pixel are not supported.");
                }

                int target = (y * width + x) * 4;
                pixels[target] = r;
                pixels[target + 1] = g;
                pixels[target + 2] = b;
                pixels[target + 3] = a;
            }
        }

        // 32 bits with all of the fourth bytes zero: no alpha was meant
        bool opaque = bitCount == 32 && compression == BI_RGB && !anyAlpha;

        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (opaque)
            {
                pixels[i + 3] = 255;
                continue;
            }

            int alpha = pixels[i + 3];
            if (alpha == 255) continue;

            pixels[i] = (byte)((pixels[i] * alpha + 127) / 255);
            pixels[i + 1] = (byte)((pixels[i + 1] * alpha + 127) / 255);
            pixels[i + 2] = (byte)((pixels[i + 2] * alpha + 127) / 255);
        }

        return (width, height, pixels);
    }

    /// <summary>A channel by its mask, stretched to 8 bits.</summary>
    private static byte Channel(uint value, uint mask)
    {
        if (mask == 0) return 0;

        int shift = BitOperations.TrailingZeroCount(mask);
        int bits = BitOperations.PopCount(mask);
        uint raw = (value & mask) >> shift;

        return bits >= 8 ? (byte)(raw >> (bits - 8)) : (byte)(raw * 255 / ((1u << bits) - 1));
    }
}