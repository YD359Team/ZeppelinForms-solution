using System.Buffers.Binary;

namespace ZeppelinForms.Gles.Text.OpenType;

/// <summary>A glyph's color picture from a CBDT table: a PNG at the strike's size, and
/// where it stands against the pen, in the strike's pixels, y up from the baseline.</summary>
internal sealed record ColorBitmap(byte[] Png, int Width, int Height, int BearingX, int BearingY, int PixelsPerEm);

/// <summary>
/// The color bitmaps of an emoji font — Google's CBLC/CBDT tables, Noto Color Emoji's: PNG
/// pictures at one or a few sizes, located by the glyph through the CBLC index.
/// </summary>
/// <remarks>The largest strike is used for every size: the renderer scales the picture
/// down with mipmaps, which looks better than a smaller strike scaled up.</remarks>
internal sealed class ColorBitmaps
{
    private readonly byte[] _data;
    private readonly int _cbdt;
    private readonly int _cbdtLength;
    private readonly List<Subtable> _subtables = [];
    private readonly int _ppem;

    private readonly record struct Subtable(int FirstGlyph, int LastGlyph, int Offset);

    private ColorBitmaps(byte[] data, int cbdt, int cbdtLength, int ppem)
    {
        _data = data;
        _cbdt = cbdt;
        _cbdtLength = cbdtLength;
        _ppem = ppem;
    }

    public static ColorBitmaps? TryRead(byte[] data, int cblc, int cblcLength, int cbdt, int cbdtLength)
    {
        try
        {
            return Read(data, cblc, cblcLength, cbdt, cbdtLength);
        }
        catch (Exception e) when (e is ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    private static ColorBitmaps? Read(byte[] data, int cblc, int cblcLength, int cbdt, int cbdtLength)
    {
        ReadOnlySpan<byte> table = data.AsSpan(cblc, cblcLength);

        int sizes = (int)BinaryPrimitives.ReadUInt32BigEndian(table[4..]);
        if (sizes <= 0) return null;

        // the strike of the most pixels per em
        int best = -1, bestPpem = 0;

        for (int i = 0; i < sizes; i++)
        {
            int record = 8 + i * 48;
            if (record + 48 > table.Length) break;

            int ppem = table[record + 45];

            if (ppem > bestPpem)
            {
                bestPpem = ppem;
                best = record;
            }
        }

        if (best < 0) return null;

        int arrayOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(table[best..]);
        int subtableCount = (int)BinaryPrimitives.ReadUInt32BigEndian(table[(best + 8)..]);

        var bitmaps = new ColorBitmaps(data, cbdt, cbdtLength, bestPpem);

        for (int i = 0; i < subtableCount; i++)
        {
            int entry = arrayOffset + i * 8;
            if (entry + 8 > table.Length) break;

            int first = BinaryPrimitives.ReadUInt16BigEndian(table[entry..]);
            int last = BinaryPrimitives.ReadUInt16BigEndian(table[(entry + 2)..]);
            int additional = (int)BinaryPrimitives.ReadUInt32BigEndian(table[(entry + 4)..]);

            bitmaps._subtables.Add(new Subtable(first, last, cblc + arrayOffset + additional));
        }

        return bitmaps;
    }

    public ColorBitmap? Get(ushort glyph)
    {
        foreach (Subtable subtable in _subtables)
        {
            if (glyph < subtable.FirstGlyph || glyph > subtable.LastGlyph) continue;

            try
            {
                return Read(subtable, glyph);
            }
            catch (Exception e) when (e is ArgumentOutOfRangeException or IndexOutOfRangeException)
            {
                return null;
            }
        }

        return null;
    }

    private ColorBitmap? Read(Subtable subtable, ushort glyph)
    {
        ReadOnlySpan<byte> data = _data;
        int o = subtable.Offset;

        int indexFormat = BinaryPrimitives.ReadUInt16BigEndian(data[o..]);
        int imageFormat = BinaryPrimitives.ReadUInt16BigEndian(data[(o + 2)..]);
        int imageData = (int)BinaryPrimitives.ReadUInt32BigEndian(data[(o + 4)..]);

        int index = glyph - subtable.FirstGlyph;
        int start, end;
        int bigMetrics = -1;

        switch (indexFormat)
        {
            case 1:
                start = (int)BinaryPrimitives.ReadUInt32BigEndian(data[(o + 8 + index * 4)..]);
                end = (int)BinaryPrimitives.ReadUInt32BigEndian(data[(o + 8 + index * 4 + 4)..]);
                break;

            case 3:
                start = BinaryPrimitives.ReadUInt16BigEndian(data[(o + 8 + index * 2)..]);
                end = BinaryPrimitives.ReadUInt16BigEndian(data[(o + 8 + index * 2 + 2)..]);
                break;

            case 2:
                {
                    int size = (int)BinaryPrimitives.ReadUInt32BigEndian(data[(o + 8)..]);
                    bigMetrics = o + 12;
                    start = index * size;
                    end = start + size;
                    break;
                }

            case 4:
                {
                    int count = (int)BinaryPrimitives.ReadUInt32BigEndian(data[(o + 8)..]);
                    start = end = -1;

                    for (int i = 0; i < count; i++)
                    {
                        int pair = o + 12 + i * 4;

                        if (BinaryPrimitives.ReadUInt16BigEndian(data[pair..]) == glyph)
                        {
                            start = BinaryPrimitives.ReadUInt16BigEndian(data[(pair + 2)..]);
                            end = BinaryPrimitives.ReadUInt16BigEndian(data[(pair + 6)..]);
                            break;
                        }
                    }

                    if (start < 0) return null;
                    break;
                }

            case 5:
                {
                    int size = (int)BinaryPrimitives.ReadUInt32BigEndian(data[(o + 8)..]);
                    bigMetrics = o + 12;
                    int count = (int)BinaryPrimitives.ReadUInt32BigEndian(data[(o + 20)..]);
                    int found = -1;

                    for (int i = 0; i < count; i++)
                    {
                        if (BinaryPrimitives.ReadUInt16BigEndian(data[(o + 24 + i * 2)..]) == glyph)
                        {
                            found = i;
                            break;
                        }
                    }

                    if (found < 0) return null;

                    start = found * size;
                    end = start + size;
                    break;
                }

            default:
                return null;
        }

        if (end <= start) return null;

        int at = _cbdt + imageData + start;
        if (at + (end - start) > _cbdt + _cbdtLength) return null;

        int width, height, bearingX, bearingY;
        int png;

        switch (imageFormat)
        {
            case 17:
                // small metrics: height, width, bearing x, bearing y, advance
                height = data[at];
                width = data[at + 1];
                bearingX = (sbyte)data[at + 2];
                bearingY = (sbyte)data[at + 3];
                png = at + 5;
                break;

            case 18:
                height = data[at];
                width = data[at + 1];
                bearingX = (sbyte)data[at + 2];
                bearingY = (sbyte)data[at + 3];
                png = at + 8;
                break;

            case 19:
                if (bigMetrics < 0) return null;

                height = data[bigMetrics];
                width = data[bigMetrics + 1];
                bearingX = (sbyte)data[bigMetrics + 2];
                bearingY = (sbyte)data[bigMetrics + 3];
                png = at;
                break;

            default:
                return null;
        }

        int length = (int)BinaryPrimitives.ReadUInt32BigEndian(data[png..]);
        png += 4;

        if (length <= 0 || png + length > at + (end - start)) return null;

        return new ColorBitmap(data.Slice(png, length).ToArray(), width, height, bearingX, bearingY, _ppem);
    }
}