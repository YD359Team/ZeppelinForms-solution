using System.Buffers.Binary;

namespace ZeppelinForms.Gles.Text.OpenType;

/// <summary>
/// The cmap of a font: a character into its glyph, through the best subtable the font has
/// — one reaching beyond the basic plane (format 12), the basic Unicode one (format 4),
/// the small old ones (6, 0), a symbol font's last.
/// </summary>
/// <remarks>It reads from any array that holds the table: a face's whole file, or just
/// the table, as the catalog reads it to look for a fallback without loading the file.</remarks>
internal sealed class CharacterMap
{
    public static readonly CharacterMap None = new([], -1, -1, false);

    private readonly byte[] _data;
    private readonly int _offset;
    private readonly int _format;
    private readonly bool _isSymbol;

    private CharacterMap(byte[] data, int offset, int format, bool isSymbol)
    {
        _data = data;
        _offset = offset;
        _format = format;
        _isSymbol = isSymbol;
    }

    /// <summary>The map of a cmap table at a place in an array.</summary>
    public static CharacterMap Read(byte[] data, int tableOffset, int tableLength)
    {
        ReadOnlySpan<byte> table = data.AsSpan(tableOffset, tableLength);
        if (table.Length < 4) return None;

        int count = BinaryPrimitives.ReadUInt16BigEndian(table[2..]);

        int best = -1, bestFormat = -1, bestRank = int.MaxValue;
        bool symbol = false;

        for (int i = 0; i < count; i++)
        {
            int record = 4 + i * 8;
            if (record + 8 > table.Length) break;

            int platform = BinaryPrimitives.ReadUInt16BigEndian(table[record..]);
            int encoding = BinaryPrimitives.ReadUInt16BigEndian(table[(record + 2)..]);
            int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(table[(record + 4)..]);

            if (offset < 0 || offset + 2 > table.Length) continue;

            int format = BinaryPrimitives.ReadUInt16BigEndian(table[offset..]);

            int rank = (platform, encoding, format) switch
            {
                (3, 10, 12) or (0, 4, 12) or (0, 6, 12) => 0,
                (0, _, 12) => 1,
                (3, 1, 4) or (0, 3, 4) => 2,
                (0, _, 4) => 3,
                (3, 1, 6) or (0, _, 6) => 4,
                (3, 0, 4) => 5,
                (1, 0, 0) or (1, 0, 6) => 6,
                _ => int.MaxValue,
            };

            if (rank < bestRank)
            {
                bestRank = rank;
                best = tableOffset + offset;
                bestFormat = format;
                symbol = platform == 3 && encoding == 0;
            }
        }

        return best < 0 ? None : new CharacterMap(data, best, bestFormat, symbol);
    }

    /// <summary>The glyph of a character; 0 — the font has none.</summary>
    public ushort GlyphFor(int codepoint)
    {
        if (_offset < 0 || codepoint < 0) return 0;

        try
        {
            ushort glyph = Lookup(codepoint);

            // a symbol font maps its characters into the private area at F000
            if (glyph == 0 && _isSymbol && codepoint < 0x100)
                glyph = Lookup(codepoint | 0xF000);

            return glyph;
        }
        catch (ArgumentOutOfRangeException)
        {
            // a subtable that claims more than the file has: no glyph rather than a failure
            return 0;
        }
    }

    private ushort Lookup(int codepoint)
    {
        ReadOnlySpan<byte> data = _data;
        int o = _offset;

        switch (_format)
        {
            case 4:
                {
                    if (codepoint > 0xFFFF) return 0;

                    int segCountX2 = BinaryPrimitives.ReadUInt16BigEndian(data[(o + 6)..]);
                    int segCount = segCountX2 / 2;
                    int endCodes = o + 14;
                    int startCodes = endCodes + segCountX2 + 2;
                    int deltas = startCodes + segCountX2;
                    int rangeOffsets = deltas + segCountX2;

                    // the first segment whose end is at or past the character
                    int lo = 0, hi = segCount - 1;

                    while (lo < hi)
                    {
                        int mid = (lo + hi) / 2;

                        if (BinaryPrimitives.ReadUInt16BigEndian(data[(endCodes + mid * 2)..]) < codepoint) lo = mid + 1;
                        else hi = mid;
                    }

                    int start = BinaryPrimitives.ReadUInt16BigEndian(data[(startCodes + lo * 2)..]);
                    int end = BinaryPrimitives.ReadUInt16BigEndian(data[(endCodes + lo * 2)..]);

                    if (codepoint < start || codepoint > end) return 0;

                    int delta = BinaryPrimitives.ReadInt16BigEndian(data[(deltas + lo * 2)..]);
                    int rangeOffset = BinaryPrimitives.ReadUInt16BigEndian(data[(rangeOffsets + lo * 2)..]);

                    if (rangeOffset == 0) return (ushort)(codepoint + delta);

                    int address = rangeOffsets + lo * 2 + rangeOffset + (codepoint - start) * 2;
                    if (address + 2 > data.Length) return 0;

                    int glyph = BinaryPrimitives.ReadUInt16BigEndian(data[address..]);
                    return glyph == 0 ? (ushort)0 : (ushort)(glyph + delta);
                }

            case 12:
                {
                    int groups = (int)BinaryPrimitives.ReadUInt32BigEndian(data[(o + 12)..]);
                    int lo = 0, hi = groups - 1;

                    while (lo <= hi)
                    {
                        int mid = (lo + hi) / 2;
                        int group = o + 16 + mid * 12;

                        uint start = BinaryPrimitives.ReadUInt32BigEndian(data[group..]);
                        uint end = BinaryPrimitives.ReadUInt32BigEndian(data[(group + 4)..]);

                        if ((uint)codepoint < start) hi = mid - 1;
                        else if ((uint)codepoint > end) lo = mid + 1;
                        else return (ushort)(BinaryPrimitives.ReadUInt32BigEndian(data[(group + 8)..]) + ((uint)codepoint - start));
                    }

                    return 0;
                }

            case 6:
                {
                    int first = BinaryPrimitives.ReadUInt16BigEndian(data[(o + 6)..]);
                    int count = BinaryPrimitives.ReadUInt16BigEndian(data[(o + 8)..]);

                    int index = codepoint - first;
                    return index >= 0 && index < count ? BinaryPrimitives.ReadUInt16BigEndian(data[(o + 10 + index * 2)..]) : (ushort)0;
                }

            case 0:
                return codepoint is >= 0 and < 256 ? data[o + 6 + codepoint] : (ushort)0;

            default:
                return 0;
        }
    }
}