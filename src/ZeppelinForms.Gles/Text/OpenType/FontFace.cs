using System.Buffers.Binary;
using System.Collections.Concurrent;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Gles.Geometry;

namespace ZeppelinForms.Gles.Text.OpenType;

/// <summary>
/// One face of an OpenType font, read in managed code: its names and style, its line
/// metrics, the glyph of a character, a glyph's advance, outline and bounds — TrueType's
/// quadratic outlines and CFF's cubic ones — and the color bitmaps of emoji fonts.
/// </summary>
/// <remarks>
/// <para>
/// All in font units, y up, as the font has them: the renderer scales and flips them for
/// the size it draws. Outlines and bounds are made once per glyph and kept; the cache is
/// safe to use from several threads, as everything else here is read only.
/// </para>
/// <para>
/// A variable font is read at its default instance: the weight axis is not applied, and a
/// bold asked of it is synthesized, as Skia does for a face without the weight.
/// </para>
/// </remarks>
internal sealed class FontFace
{
    private static int s_nextId;

    private readonly Dictionary<uint, (int Offset, int Length)> _tables = [];
    private readonly ConcurrentDictionary<ushort, VectorPath?> _outlines = new();
    private readonly ConcurrentDictionary<ushort, Rectangle> _bounds = new();

    private readonly CharacterMap _cmap;

    private readonly int _hmtxOffset;
    private readonly int _numberOfHMetrics;

    private readonly int _locaOffset;
    private readonly int _glyfOffset;
    private readonly bool _longLoca;

    private readonly CffOutlines? _cff;
    private readonly ColorBitmaps? _bitmaps;

    private FontFace(FontFile file, int index)
    {
        File = file;
        Index = index;
        Id = Interlocked.Increment(ref s_nextId);

        ReadOnlySpan<byte> data = file.Data;
        int directory = file.FaceOffset(index);

        if (directory + 12 > data.Length) throw new InvalidDataException($"A face beyond the end of {file.Name}.");

        int numTables = BinaryPrimitives.ReadUInt16BigEndian(data[(directory + 4)..]);

        for (int i = 0; i < numTables; i++)
        {
            int record = directory + 12 + i * 16;
            if (record + 16 > data.Length) break;

            uint tag = BinaryPrimitives.ReadUInt32BigEndian(data[record..]);
            int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(data[(record + 8)..]);
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(data[(record + 12)..]);

            // a table claiming more than the file has is left out, not trusted
            if (offset >= 0 && length >= 0 && (long)offset + length <= data.Length)
                _tables[tag] = (offset, length);
        }

        ReadOnlySpan<byte> head = Table(Tag("head"));
        if (head.Length < 54) throw new InvalidDataException($"A face without a head table in {file.Name}.");

        UnitsPerEm = BinaryPrimitives.ReadUInt16BigEndian(head[18..]);
        if (UnitsPerEm == 0) UnitsPerEm = 1000;

        int macStyle = BinaryPrimitives.ReadUInt16BigEndian(head[44..]);
        _longLoca = BinaryPrimitives.ReadInt16BigEndian(head[50..]) != 0;

        ReadOnlySpan<byte> maxp = Table(Tag("maxp"));
        NumGlyphs = maxp.Length >= 6 ? BinaryPrimitives.ReadUInt16BigEndian(maxp[4..]) : 0;

        ReadOnlySpan<byte> hhea = Table(Tag("hhea"));

        if (hhea.Length >= 36)
        {
            Ascender = BinaryPrimitives.ReadInt16BigEndian(hhea[4..]);
            Descender = BinaryPrimitives.ReadInt16BigEndian(hhea[6..]);
            LineGap = BinaryPrimitives.ReadInt16BigEndian(hhea[8..]);
            _numberOfHMetrics = BinaryPrimitives.ReadUInt16BigEndian(hhea[34..]);
        }

        ReadOnlySpan<byte> os2 = Table(Tag("OS/2"));

        Weight = 400;

        if (os2.Length >= 78)
        {
            Weight = BinaryPrimitives.ReadUInt16BigEndian(os2[4..]);

            int fsSelection = BinaryPrimitives.ReadUInt16BigEndian(os2[62..]);

            // italic (bit 0) or oblique (bit 9)
            IsItalic = (fsSelection & 0x0201) != 0;

            // as FreeType: the typographic metrics only where the horizontal header has none
            if (Ascender == 0 && Descender == 0)
            {
                Ascender = BinaryPrimitives.ReadInt16BigEndian(os2[68..]);
                Descender = BinaryPrimitives.ReadInt16BigEndian(os2[70..]);
                LineGap = BinaryPrimitives.ReadInt16BigEndian(os2[72..]);

                if (Ascender == 0 && Descender == 0)
                {
                    Ascender = BinaryPrimitives.ReadUInt16BigEndian(os2[74..]);
                    Descender = -BinaryPrimitives.ReadUInt16BigEndian(os2[76..]);
                }
            }
        }
        else
        {
            IsItalic = (macStyle & 0x02) != 0;
            if ((macStyle & 0x01) != 0) Weight = 700;
        }

        // a weight class of 1 to 9 is the old scale of hundreds some fonts still carry
        if (Weight is > 0 and < 10) Weight *= 100;

        _hmtxOffset = _tables.TryGetValue(Tag("hmtx"), out var hmtx) ? hmtx.Offset : -1;

        if (_tables.TryGetValue(Tag("loca"), out var loca) && _tables.TryGetValue(Tag("glyf"), out var glyf))
        {
            _locaOffset = loca.Offset;
            _glyfOffset = glyf.Offset;
            HasOutlines = true;
        }
        else
        {
            _locaOffset = _glyfOffset = -1;
        }

        if (!HasOutlines && _tables.TryGetValue(Tag("CFF "), out var cff))
        {
            _cff = CffOutlines.TryRead(data.Slice(cff.Offset, cff.Length).ToArray());
            HasOutlines = _cff is not null;
        }

        if (_tables.TryGetValue(Tag("CBLC"), out var cblc) && _tables.TryGetValue(Tag("CBDT"), out var cbdt))
            _bitmaps = ColorBitmaps.TryRead(file.Data, cblc.Offset, cblc.Length, cbdt.Offset, cbdt.Length);

        _cmap = _tables.TryGetValue(Tag("cmap"), out var cmap)
            ? CharacterMap.Read(file.Data, cmap.Offset, cmap.Length)
            : CharacterMap.None;

        ReadNames();
    }

    /// <summary>A face of a file; null when the face can't be read or has nothing to draw.</summary>
    public static FontFace? TryOpen(FontFile file, int index)
    {
        try
        {
            var face = new FontFace(file, index);
            return face.HasOutlines || face.HasColorBitmaps ? face : null;
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    public FontFile File { get; }

    public int Index { get; }

    /// <summary>Unique in the process: what caches of glyphs are keyed by.</summary>
    public int Id { get; }

    public int UnitsPerEm { get; }

    public int Ascender { get; }

    /// <summary>Negative: below the baseline, as the font has it.</summary>
    public int Descender { get; }

    public int LineGap { get; }

    public int NumGlyphs { get; }

    /// <summary>The OpenType weight: 400 regular, 700 bold.</summary>
    public int Weight { get; }

    public bool IsItalic { get; }

    public bool HasOutlines { get; }

    public bool HasColorBitmaps => _bitmaps is not null;

    /// <summary>The family as the font names it for people: the typographic family where it
    /// has one ("Inter"), the legacy one otherwise ("Inter Medium").</summary>
    public string Family { get; private set; } = string.Empty;

    /// <summary>Every family name of the face, in every language and both kinds: a request
    /// for any of them finds it.</summary>
    public IReadOnlyList<string> FamilyNames { get; private set; } = [];

    public override string ToString() => $"{Family} {Weight}{(IsItalic ? " italic" : "")} ({System.IO.Path.GetFileName(File.Name)}#{Index})";

    #region Tables

    public static uint Tag(string tag) =>
        (uint)(tag[0] << 24 | tag[1] << 16 | tag[2] << 8 | tag[3]);

    public bool HasTable(string tag) => _tables.ContainsKey(Tag(tag));

    private ReadOnlySpan<byte> Table(uint tag) =>
        _tables.TryGetValue(tag, out var table)
            ? File.Data.AsSpan(table.Offset, table.Length)
            : [];

    #endregion

    #region Names

    private void ReadNames()
    {
        List<string> families = Fonts.FontCatalog.ReadFamilies(Table(Tag("name")));

        // the typographic family first ("Inter"), the legacy one after ("Inter Medium")
        Family = families.Count > 0 ? families[0] : System.IO.Path.GetFileNameWithoutExtension(File.Name);
        FamilyNames = families.Count > 0 ? families : [Family];
    }

    #endregion

    #region Character map

    /// <summary>The glyph of a character; 0 — the font has none.</summary>
    public ushort GlyphFor(int codepoint) => _cmap.GlyphFor(codepoint);

    #endregion

    #region Metrics

    /// <summary>A glyph's advance in font units.</summary>
    public int Advance(ushort glyph)
    {
        if (_hmtxOffset < 0 || _numberOfHMetrics == 0) return UnitsPerEm / 2;

        int index = Math.Min(glyph, _numberOfHMetrics - 1);
        int at = _hmtxOffset + index * 4;

        return at + 2 <= File.Data.Length ? BinaryPrimitives.ReadUInt16BigEndian(File.Data.AsSpan(at)) : 0;
    }

    /// <summary>A glyph's ink in font units, y up: its outline's control box, as FreeType
    /// and Skia measure it — a TrueType glyph's box from its header, every point of it, a
    /// stray anchor point too; a color bitmap's box for an emoji.</summary>
    public Rectangle Bounds(ushort glyph) => _bounds.GetOrAdd(glyph, static (g, face) => face.ComputeBounds(g), this);

    private Rectangle ComputeBounds(ushort glyph)
    {
        if (_glyfOffset >= 0 && (glyph < NumGlyphs || NumGlyphs == 0))
        {
            try
            {
                (int offset, int length) = GlyphLocation(glyph);

                if (length >= 10)
                {
                    ReadOnlySpan<byte> header = File.Data.AsSpan(offset, 10);

                    int xMin = BinaryPrimitives.ReadInt16BigEndian(header[2..]);
                    int yMin = BinaryPrimitives.ReadInt16BigEndian(header[4..]);
                    int xMax = BinaryPrimitives.ReadInt16BigEndian(header[6..]);
                    int yMax = BinaryPrimitives.ReadInt16BigEndian(header[8..]);

                    return new Rectangle(xMin, yMin, xMax - xMin, yMax - yMin);
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                return Rectangle.Empty;
            }
        }

        if (Outline(glyph) is { IsEmpty: false } outline) return outline.ControlBounds;

        if (_bitmaps?.Get(glyph) is { } bitmap)
        {
            float scale = (float)UnitsPerEm / bitmap.PixelsPerEm;
            return new Rectangle(bitmap.BearingX * scale, (bitmap.BearingY - bitmap.Height) * scale, bitmap.Width * scale, bitmap.Height * scale);
        }

        return Rectangle.Empty;
    }

    #endregion

    #region Outlines

    /// <summary>A glyph's outline in font units, y up; null — it has none (a space).</summary>
    public VectorPath? Outline(ushort glyph)
    {
        if (glyph >= NumGlyphs && NumGlyphs > 0) return null;

        return _outlines.GetOrAdd(glyph, static (g, face) => face.ReadOutline(g), this);
    }

    private VectorPath? ReadOutline(ushort glyph)
    {
        try
        {
            VectorPath? path = _cff is not null
                ? _cff.Outline(glyph)
                : _glyfOffset >= 0 ? TrueTypeOutline(glyph, 0) : null;

            return path is { IsEmpty: false } ? path : null;
        }
        catch (Exception e) when (e is ArgumentOutOfRangeException or IndexOutOfRangeException or InvalidDataException)
        {
            // a broken glyph draws as nothing rather than failing the text around it
            return null;
        }
    }

    /// <summary>Where a glyph's data is in the glyf table, and how long.</summary>
    private (int Offset, int Length) GlyphLocation(ushort glyph)
    {
        ReadOnlySpan<byte> data = File.Data;

        int start, end;

        if (_longLoca)
        {
            start = (int)BinaryPrimitives.ReadUInt32BigEndian(data[(_locaOffset + glyph * 4)..]);
            end = (int)BinaryPrimitives.ReadUInt32BigEndian(data[(_locaOffset + glyph * 4 + 4)..]);
        }
        else
        {
            start = BinaryPrimitives.ReadUInt16BigEndian(data[(_locaOffset + glyph * 2)..]) * 2;
            end = BinaryPrimitives.ReadUInt16BigEndian(data[(_locaOffset + glyph * 2 + 2)..]) * 2;
        }

        return (_glyfOffset + start, end - start);
    }

    /// <summary>A TrueType glyph: contours of on-curve points and quadratic control points
    /// between them, or a composite of other glyphs, each placed by its own transform.</summary>
    private VectorPath? TrueTypeOutline(ushort glyph, int depth)
    {
        // a composite that includes itself, directly or not, is a broken font
        if (depth > 8) return null;

        (int offset, int length) = GlyphLocation(glyph);
        if (length <= 10) return null;

        ReadOnlySpan<byte> data = File.Data.AsSpan(offset, length);
        int contours = BinaryPrimitives.ReadInt16BigEndian(data);

        return contours >= 0
            ? SimpleGlyph(data, contours)
            : CompositeGlyph(data, depth);
    }

    private static VectorPath? SimpleGlyph(ReadOnlySpan<byte> data, int contours)
    {
        if (contours == 0) return null;

        int p = 10;
        var endPoints = new int[contours];

        for (int i = 0; i < contours; i++, p += 2)
            endPoints[i] = BinaryPrimitives.ReadUInt16BigEndian(data[p..]);

        int pointCount = endPoints[^1] + 1;

        int instructions = BinaryPrimitives.ReadUInt16BigEndian(data[p..]);
        p += 2 + instructions;

        var flags = new byte[pointCount];

        for (int i = 0; i < pointCount;)
        {
            byte flag = data[p++];
            flags[i++] = flag;

            // repeated: the next byte says how many times more
            if ((flag & 0x08) != 0)
            {
                int repeat = data[p++];
                for (int r = 0; r < repeat && i < pointCount; r++) flags[i++] = flag;
            }
        }

        var xs = new int[pointCount];
        var ys = new int[pointCount];

        int x = 0;

        for (int i = 0; i < pointCount; i++)
        {
            byte flag = flags[i];

            if ((flag & 0x02) != 0)
            {
                int dx = data[p++];
                x += (flag & 0x10) != 0 ? dx : -dx;
            }
            else if ((flag & 0x10) == 0)
            {
                x += BinaryPrimitives.ReadInt16BigEndian(data[p..]);
                p += 2;
            }

            xs[i] = x;
        }

        int y = 0;

        for (int i = 0; i < pointCount; i++)
        {
            byte flag = flags[i];

            if ((flag & 0x04) != 0)
            {
                int dy = data[p++];
                y += (flag & 0x20) != 0 ? dy : -dy;
            }
            else if ((flag & 0x20) == 0)
            {
                y += BinaryPrimitives.ReadInt16BigEndian(data[p..]);
                p += 2;
            }

            ys[i] = y;
        }

        var path = new VectorPath();
        int first = 0;

        foreach (int last in endPoints)
        {
            AddContour(path, xs, ys, flags, first, last);
            first = last + 1;
        }

        return path;
    }

    /// <summary>One contour of quadratic segments: two control points in a row have an
    /// on-curve point implied midway between them.</summary>
    private static void AddContour(VectorPath path, int[] xs, int[] ys, byte[] flags, int first, int last)
    {
        int count = last - first + 1;
        if (count < 2) return;

        // a starting point on the curve: the first one, the last one, or — all of them
        // controls — the point implied between the last and the first
        int start;
        int steps;
        Point startPoint;

        if (OnCurve(0))
        {
            start = 0;
            steps = count - 1;
            startPoint = At(0);
        }
        else if (OnCurve(count - 1))
        {
            start = count - 1;
            steps = count - 1;
            startPoint = At(count - 1);
        }
        else
        {
            start = -1;
            steps = count;
            startPoint = Mid(At(count - 1), At(0));
        }

        path.MoveTo(startPoint);

        Point? control = null;

        for (int k = 1; k <= steps; k++)
        {
            int i = (start + k + count) % count;
            Point p = At(i);

            if (OnCurve(i))
            {
                if (control is { } c) path.QuadTo(c, p);
                else path.LineTo(p);

                control = null;
            }
            else
            {
                if (control is { } c) path.QuadTo(c, Mid(c, p));

                control = p;
            }
        }

        // back to the start, through the last control if one is pending
        if (control is { } lastControl) path.QuadTo(lastControl, startPoint);

        path.Close();

        bool OnCurve(int i) => (flags[first + i] & 0x01) != 0;

        Point At(int i) => new(xs[first + i], ys[first + i]);
    }

    private static Point Mid(Point a, Point b) => new((a.X + b.X) / 2f, (a.Y + b.Y) / 2f);

    private VectorPath? CompositeGlyph(ReadOnlySpan<byte> data, int depth)
    {
        const int ArgsAreWords = 0x0001;
        const int ArgsAreXyValues = 0x0002;
        const int HaveScale = 0x0008;
        const int MoreComponents = 0x0020;
        const int HaveXyScale = 0x0040;
        const int HaveTwoByTwo = 0x0080;
        const int ScaledComponentOffset = 0x0800;

        var path = new VectorPath();
        int p = 10;
        int flags;

        do
        {
            flags = BinaryPrimitives.ReadUInt16BigEndian(data[p..]);
            ushort component = BinaryPrimitives.ReadUInt16BigEndian(data[(p + 2)..]);
            p += 4;

            float dx, dy;

            if ((flags & ArgsAreWords) != 0)
            {
                dx = BinaryPrimitives.ReadInt16BigEndian(data[p..]);
                dy = BinaryPrimitives.ReadInt16BigEndian(data[(p + 2)..]);
                p += 4;
            }
            else
            {
                dx = (sbyte)data[p];
                dy = (sbyte)data[p + 1];
                p += 2;
            }

            // matched points instead of offsets: rare, and placed at the origin here
            if ((flags & ArgsAreXyValues) == 0) dx = dy = 0;

            float a = 1, b = 0, c = 0, d = 1;

            if ((flags & HaveScale) != 0)
            {
                a = d = F2Dot14(data[p..]);
                p += 2;
            }
            else if ((flags & HaveXyScale) != 0)
            {
                a = F2Dot14(data[p..]);
                d = F2Dot14(data[(p + 2)..]);
                p += 4;
            }
            else if ((flags & HaveTwoByTwo) != 0)
            {
                a = F2Dot14(data[p..]);
                b = F2Dot14(data[(p + 2)..]);
                c = F2Dot14(data[(p + 4)..]);
                d = F2Dot14(data[(p + 6)..]);
                p += 8;
            }

            if ((flags & ScaledComponentOffset) != 0)
            {
                (dx, dy) = (a * dx + c * dy, b * dx + d * dy);
            }

            if (component != 0 || depth == 0)
            {
                VectorPath? part = TrueTypeOutline(component, depth + 1);

                if (part is not null)
                    path.Append(part, new Affine(a, b, c, d, dx, dy));
            }
        }
        while ((flags & MoreComponents) != 0);

        return path;

        static float F2Dot14(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadInt16BigEndian(bytes) / 16384f;
    }

    #endregion

    #region Color bitmaps

    /// <summary>A glyph's color bitmap, an emoji's picture; null — it has none.</summary>
    public ColorBitmap? ColorBitmap(ushort glyph) => _bitmaps?.Get(glyph);

    #endregion
}