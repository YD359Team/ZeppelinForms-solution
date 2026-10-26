using System.Buffers.Binary;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Gles.Geometry;

namespace ZeppelinForms.Gles.Text.OpenType;

/// <summary>
/// The outlines of a CFF table — OpenType fonts with PostScript outlines, .otf files and the
/// CJK collections — through an interpreter of Type 2 charstrings.
/// </summary>
/// <remarks>
/// <para>
/// A charstring is a small stack program drawing a glyph with lines and cubic curves; it
/// calls subroutines local to its font and global to the table. A CID-keyed font (CJK)
/// has several private dictionaries, each with its own subroutines, and a selector saying
/// which glyph uses which.
/// </para>
/// <para>
/// Hints are counted, not applied: their masks must be skipped by the right number of
/// bytes, and the renderer draws unhinted, as Skia does with slight hinting. CFF2, the
/// variable flavor, is not read.
/// </para>
/// </remarks>
internal sealed class CffOutlines
{
    private readonly byte[] _data;
    private readonly Index _charStrings;
    private readonly Index _globalSubrs;
    private readonly Index[] _localSubrs;

    /// <summary>For a CID-keyed font: the private dictionary of each glyph; null — one for all.</summary>
    private readonly byte[]? _fdSelect;

    private CffOutlines(byte[] data, Index charStrings, Index globalSubrs, Index[] localSubrs, byte[]? fdSelect)
    {
        _data = data;
        _charStrings = charStrings;
        _globalSubrs = globalSubrs;
        _localSubrs = localSubrs;
        _fdSelect = fdSelect;
    }

    /// <summary>The table read, or null when it is broken or of a kind not supported.</summary>
    public static CffOutlines? TryRead(byte[] data)
    {
        try
        {
            return Read(data);
        }
        catch (Exception e) when (e is ArgumentOutOfRangeException or IndexOutOfRangeException or InvalidDataException)
        {
            return null;
        }
    }

    private static CffOutlines? Read(byte[] data)
    {
        if (data.Length < 4 || data[0] != 1) return null;

        int headerSize = data[2];

        Index names = Index.Read(data, headerSize);
        Index topDicts = Index.Read(data, names.End);
        Index strings = Index.Read(data, topDicts.End);
        Index globalSubrs = Index.Read(data, strings.End);

        if (topDicts.Count == 0) return null;

        Dictionary<int, float[]> top = ReadDict(topDicts.Item(data, 0));

        // a charstring type other than 2 is the old Type 1 kind, which OpenType never has
        if (top.TryGetValue(Escape(6), out float[]? type) && type.Length > 0 && (int)type[0] != 2) return null;

        if (!top.TryGetValue(17, out float[]? charStringsOffset) || charStringsOffset.Length == 0) return null;

        Index charStrings = Index.Read(data, (int)charStringsOffset[0]);

        // a CID-keyed font: a dictionary per group of glyphs, each with its own private one
        if (top.TryGetValue(Escape(36), out float[]? fdArrayOffset) && top.TryGetValue(Escape(37), out float[]? fdSelectOffset))
        {
            Index fdArray = Index.Read(data, (int)fdArrayOffset[0]);
            var locals = new Index[fdArray.Count];

            for (int i = 0; i < fdArray.Count; i++)
                locals[i] = LocalSubrs(data, ReadDict(fdArray.Item(data, i)));

            byte[] select = ReadFdSelect(data, (int)fdSelectOffset[0], charStrings.Count);

            return new CffOutlines(data, charStrings, globalSubrs, locals, select);
        }

        return new CffOutlines(data, charStrings, globalSubrs, [LocalSubrs(data, top)], null);
    }

    /// <summary>The subroutines of a private dictionary, found through the dictionary that
    /// points at it; none where it has none.</summary>
    private static Index LocalSubrs(byte[] data, Dictionary<int, float[]> dict)
    {
        if (!dict.TryGetValue(18, out float[]? @private) || @private.Length < 2) return Index.Empty;

        int size = (int)@private[0];
        int offset = (int)@private[1];

        if (offset <= 0 || size <= 0 || offset + size > data.Length) return Index.Empty;

        Dictionary<int, float[]> privateDict = ReadDict(data.AsSpan(offset, size));

        // the subroutines' offset is from the private dictionary's start
        return privateDict.TryGetValue(19, out float[]? subrs) && subrs.Length > 0
            ? Index.Read(data, offset + (int)subrs[0])
            : Index.Empty;
    }

    private static byte[] ReadFdSelect(byte[] data, int offset, int glyphCount)
    {
        var select = new byte[glyphCount];
        int format = data[offset];

        if (format == 0)
        {
            data.AsSpan(offset + 1, glyphCount).CopyTo(select);
        }
        else if (format == 3)
        {
            int ranges = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset + 1));
            int p = offset + 3;

            for (int r = 0; r < ranges; r++, p += 3)
            {
                int first = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(p));
                byte fd = data[p + 2];
                int next = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(p + 3));

                for (int g = first; g < next && g < glyphCount; g++) select[g] = fd;
            }
        }
        else
        {
            throw new InvalidDataException($"Unknown FDSelect format {format}.");
        }

        return select;
    }

    private static int Escape(int op) => 1200 + op;

    /// <summary>A DICT: operands then an operator, over and over.</summary>
    private static Dictionary<int, float[]> ReadDict(ReadOnlySpan<byte> dict)
    {
        var result = new Dictionary<int, float[]>();
        var operands = new List<float>();

        int p = 0;

        while (p < dict.Length)
        {
            int b0 = dict[p];

            if (b0 <= 21)
            {
                int op = b0;
                p++;

                if (b0 == 12)
                {
                    op = Escape(dict[p]);
                    p++;
                }

                result[op] = [.. operands];
                operands.Clear();
            }
            else if (b0 == 28)
            {
                operands.Add(BinaryPrimitives.ReadInt16BigEndian(dict[(p + 1)..]));
                p += 3;
            }
            else if (b0 == 29)
            {
                operands.Add(BinaryPrimitives.ReadInt32BigEndian(dict[(p + 1)..]));
                p += 5;
            }
            else if (b0 == 30)
            {
                // a real in packed decimal nibbles; read only to be skipped past correctly
                p++;
                operands.Add(ReadReal(dict, ref p));
            }
            else if (b0 is >= 32 and <= 246)
            {
                operands.Add(b0 - 139);
                p++;
            }
            else if (b0 is >= 247 and <= 250)
            {
                operands.Add((b0 - 247) * 256 + dict[p + 1] + 108);
                p += 2;
            }
            else if (b0 is >= 251 and <= 254)
            {
                operands.Add(-(b0 - 251) * 256 - dict[p + 1] - 108);
                p += 2;
            }
            else
            {
                p++;
            }
        }

        return result;
    }

    private static float ReadReal(ReadOnlySpan<byte> dict, ref int p)
    {
        var text = new System.Text.StringBuilder();

        while (p < dict.Length)
        {
            byte b = dict[p++];

            foreach (int nibble in (ReadOnlySpan<int>)[b >> 4, b & 0x0F])
            {
                switch (nibble)
                {
                    case <= 9: text.Append((char)('0' + nibble)); break;
                    case 0xA: text.Append('.'); break;
                    case 0xB: text.Append('E'); break;
                    case 0xC: text.Append("E-"); break;
                    case 0xE: text.Append('-'); break;
                    case 0xF:
                        return float.TryParse(text.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value) ? value : 0;
                }
            }
        }

        return 0;
    }

    /// <summary>A glyph's outline in font units, y up.</summary>
    public VectorPath? Outline(ushort glyph)
    {
        if (glyph >= _charStrings.Count) return null;

        int fd = _fdSelect is null ? 0 : _fdSelect[glyph];
        Index local = fd < _localSubrs.Length ? _localSubrs[fd] : Index.Empty;

        var interpreter = new Interpreter(_data, _globalSubrs, local);
        interpreter.Run(_charStrings.Item(_data, glyph), 0);
        interpreter.Finish();

        return interpreter.Path;
    }

    /// <summary>An INDEX: a count, the size of its offsets, the offsets, the data.</summary>
    private readonly record struct Index(int Count, int OffSize, int OffsetsStart, int DataStart, int End)
    {
        public static readonly Index Empty = new(0, 0, 0, 0, 0);

        public static Index Read(byte[] data, int offset)
        {
            int count = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));

            if (count == 0) return new Index(0, 0, offset + 2, offset + 2, offset + 2);

            int offSize = data[offset + 2];
            int offsets = offset + 3;
            int dataStart = offsets + (count + 1) * offSize - 1;

            int last = ReadOffset(data, offsets + count * offSize, offSize);

            return new Index(count, offSize, offsets, dataStart, dataStart + last);
        }

        public ReadOnlySpan<byte> Item(byte[] data, int index)
        {
            int start = ReadOffset(data, OffsetsStart + index * OffSize, OffSize);
            int end = ReadOffset(data, OffsetsStart + (index + 1) * OffSize, OffSize);

            return data.AsSpan(DataStart + start, end - start);
        }

        private static int ReadOffset(byte[] data, int at, int size)
        {
            int value = 0;
            for (int i = 0; i < size; i++) value = (value << 8) | data[at + i];
            return value;
        }

        /// <summary>The bias a subroutine's number is given with: its numbers are signed,
        /// to fit more of them in one byte.</summary>
        public int Bias => Count < 1240 ? 107 : Count < 33900 ? 1131 : 32768;
    }

    /// <summary>The Type 2 charstring machine: a stack of operands, a current point, the
    /// path it draws, the stems it has counted for the hint masks.</summary>
    private sealed class Interpreter(byte[] data, Index globalSubrs, Index localSubrs)
    {
        private readonly float[] _stack = new float[48];
        private int _count;

        private float _x;
        private float _y;
        private int _stems;
        private bool _widthDone;
        private bool _open;
        private bool _ended;

        public VectorPath Path { get; } = new();

        public void Finish()
        {
            if (_open) Path.Close();
            _open = false;
        }

        public void Run(ReadOnlySpan<byte> code, int depth)
        {
            if (depth > 10) throw new InvalidDataException("Charstring subroutines nest too deep.");

            int p = 0;

            while (p < code.Length && !_ended)
            {
                int b0 = code[p++];

                if (b0 >= 32 || b0 == 28)
                {
                    float value;

                    if (b0 == 28)
                    {
                        value = BinaryPrimitives.ReadInt16BigEndian(code[p..]);
                        p += 2;
                    }
                    else if (b0 <= 246)
                    {
                        value = b0 - 139;
                    }
                    else if (b0 <= 250)
                    {
                        value = (b0 - 247) * 256 + code[p++] + 108;
                    }
                    else if (b0 <= 254)
                    {
                        value = -(b0 - 251) * 256 - code[p++] - 108;
                    }
                    else
                    {
                        value = BinaryPrimitives.ReadInt32BigEndian(code[p..]) / 65536f;
                        p += 4;
                    }

                    Push(value);
                    continue;
                }

                switch (b0)
                {
                    case 1:  // hstem
                    case 3:  // vstem
                    case 18: // hstemhm
                    case 23: // vstemhm
                        Stems();
                        break;

                    case 19: // hintmask
                    case 20: // cntrmask
                        // stems given right before a mask are vertical stems without their operator
                        Stems();
                        p += (_stems + 7) / 8;
                        break;

                    case 21: // rmoveto
                        Width(_count > 2 ? 1 : 0);
                        MoveTo(Arg(0), Arg(1));
                        Clear();
                        break;

                    case 22: // hmoveto
                        Width(_count > 1 ? 1 : 0);
                        MoveTo(Arg(0), 0);
                        Clear();
                        break;

                    case 4: // vmoveto
                        Width(_count > 1 ? 1 : 0);
                        MoveTo(0, Arg(0));
                        Clear();
                        break;

                    case 5: // rlineto
                        for (int i = 0; i + 1 < _count; i += 2) LineTo(_stack[i], _stack[i + 1]);
                        Clear();
                        break;

                    case 6: // hlineto
                    case 7: // vlineto
                        {
                            bool horizontal = b0 == 6;

                            for (int i = 0; i < _count; i++)
                            {
                                if (horizontal) LineTo(_stack[i], 0);
                                else LineTo(0, _stack[i]);

                                horizontal = !horizontal;
                            }

                            Clear();
                            break;
                        }

                    case 8: // rrcurveto
                        for (int i = 0; i + 5 < _count; i += 6)
                            CurveTo(_stack[i], _stack[i + 1], _stack[i + 2], _stack[i + 3], _stack[i + 4], _stack[i + 5]);
                        Clear();
                        break;

                    case 27: // hhcurveto
                        {
                            int i = 0;
                            float dy1 = 0;

                            if (_count % 4 == 1) dy1 = _stack[i++];

                            for (; i + 3 < _count; i += 4)
                            {
                                CurveTo(_stack[i], dy1, _stack[i + 1], _stack[i + 2], _stack[i + 3], 0);
                                dy1 = 0;
                            }

                            Clear();
                            break;
                        }

                    case 26: // vvcurveto
                        {
                            int i = 0;
                            float dx1 = 0;

                            if (_count % 4 == 1) dx1 = _stack[i++];

                            for (; i + 3 < _count; i += 4)
                            {
                                CurveTo(dx1, _stack[i], _stack[i + 1], _stack[i + 2], 0, _stack[i + 3]);
                                dx1 = 0;
                            }

                            Clear();
                            break;
                        }

                    case 30: // vhcurveto
                    case 31: // hvcurveto
                        {
                            bool horizontal = b0 == 31;
                            int i = 0;

                            while (i + 3 < _count)
                            {
                                // the last curve of an odd count takes the extra operand
                                bool last = _count - i == 5;
                                float extra = last ? _stack[i + 4] : 0;

                                if (horizontal)
                                    CurveTo(_stack[i], 0, _stack[i + 1], _stack[i + 2], extra, _stack[i + 3]);
                                else
                                    CurveTo(0, _stack[i], _stack[i + 1], _stack[i + 2], _stack[i + 3], extra);

                                horizontal = !horizontal;
                                i += last ? 5 : 4;
                            }

                            Clear();
                            break;
                        }

                    case 24: // rcurveline
                        {
                            int i = 0;

                            for (; i + 7 < _count; i += 6)
                                CurveTo(_stack[i], _stack[i + 1], _stack[i + 2], _stack[i + 3], _stack[i + 4], _stack[i + 5]);

                            if (i + 1 < _count) LineTo(_stack[i], _stack[i + 1]);

                            Clear();
                            break;
                        }

                    case 25: // rlinecurve
                        {
                            int i = 0;

                            for (; i + 7 < _count; i += 2) LineTo(_stack[i], _stack[i + 1]);

                            if (i + 5 < _count)
                                CurveTo(_stack[i], _stack[i + 1], _stack[i + 2], _stack[i + 3], _stack[i + 4], _stack[i + 5]);

                            Clear();
                            break;
                        }

                    case 10: // callsubr
                    case 29: // callgsubr
                        {
                            Index subrs = b0 == 10 ? localSubrs : globalSubrs;
                            int number = (int)Pop() + subrs.Bias;

                            if (number < 0 || number >= subrs.Count)
                                throw new InvalidDataException("A charstring calls a subroutine it doesn't have.");

                            Run(subrs.Item(data, number), depth + 1);
                            break;
                        }

                    case 11: // return
                        return;

                    case 14: // endchar
                        // four more operands: the accented character of Type 1, never in OpenType
                        Width(_count is 1 or 5 ? 1 : 0);
                        Finish();
                        Clear();
                        _ended = true;
                        return;

                    case 12:
                        Flex(code[p++]);
                        break;

                    default:
                        // reserved operators clear the stack, as the specification asks
                        Clear();
                        break;
                }
            }
        }

        private void Flex(int op)
        {
            switch (op)
            {
                case 35: // flex
                    CurveTo(Arg(0), Arg(1), Arg(2), Arg(3), Arg(4), Arg(5));
                    CurveTo(Arg(6), Arg(7), Arg(8), Arg(9), Arg(10), Arg(11));
                    break;

                case 34: // hflex
                    {
                        float y0 = _y;
                        CurveTo(Arg(0), 0, Arg(1), Arg(2), Arg(3), 0);
                        CurveTo(Arg(4), 0, Arg(5), y0 - _y, Arg(6), 0);
                        break;
                    }

                case 36: // hflex1
                    {
                        float y0 = _y;
                        CurveTo(Arg(0), Arg(1), Arg(2), Arg(3), Arg(4), 0);
                        // the second curve ends level with the first one's start
                        CurveTo(Arg(5), 0, Arg(6), Arg(7), Arg(8), y0 - _y - Arg(7));
                        break;
                    }

                case 37: // flex1
                    {
                        float x0 = _x, y0 = _y;

                        float dx = Arg(0) + Arg(2) + Arg(4) + Arg(6) + Arg(8);
                        float dy = Arg(1) + Arg(3) + Arg(5) + Arg(7) + Arg(9);

                        CurveTo(Arg(0), Arg(1), Arg(2), Arg(3), Arg(4), Arg(5));

                        // the last point moves along the longer axis only
                        if (MathF.Abs(dx) > MathF.Abs(dy))
                            CurveTo(Arg(6), Arg(7), Arg(8), Arg(9), Arg(10), y0 - (_y + Arg(7) + Arg(9)));
                        else
                            CurveTo(Arg(6), Arg(7), Arg(8), Arg(9), x0 - (_x + Arg(6) + Arg(8)), Arg(10));

                        break;
                    }
            }

            Clear();
        }

        private void Stems()
        {
            // an odd count of operands has the advance width first
            Width(_count % 2);
            _stems += _count / 2;
            Clear();
        }

        /// <summary>The first stack-clearing operator may carry the glyph's width first: it
        /// is dropped, the advance comes from hmtx.</summary>
        private void Width(int extra)
        {
            if (_widthDone) return;

            _widthDone = true;

            if (extra > 0 && _count > 0)
            {
                Array.Copy(_stack, 1, _stack, 0, _count - 1);
                _count--;
            }
        }

        private void Push(float value)
        {
            if (_count < _stack.Length) _stack[_count++] = value;
        }

        private float Pop() => _count > 0 ? _stack[--_count] : 0;

        private float Arg(int i) => i < _count ? _stack[i] : 0;

        private void Clear() => _count = 0;

        private void MoveTo(float dx, float dy)
        {
            if (_open) Path.Close();

            _x += dx;
            _y += dy;

            Path.MoveTo(new Point(_x, _y));
            _open = true;
        }

        private void LineTo(float dx, float dy)
        {
            _x += dx;
            _y += dy;

            Path.LineTo(new Point(_x, _y));
        }

        private void CurveTo(float dx1, float dy1, float dx2, float dy2, float dx3, float dy3)
        {
            float x1 = _x + dx1, y1 = _y + dy1;
            float x2 = x1 + dx2, y2 = y1 + dy2;

            _x = x2 + dx3;
            _y = y2 + dy3;

            Path.CubicTo(new Point(x1, y1), new Point(x2, y2), new Point(_x, _y));
        }
    }
}