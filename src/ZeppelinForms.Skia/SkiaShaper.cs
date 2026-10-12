using HarfBuzzSharp;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using HbBuffer = HarfBuzzSharp.Buffer;
using HbFont = HarfBuzzSharp.Font;

namespace ZeppelinForms.Skia;

/// <summary>The glyphs of a shaped piece of a line, in visual order, and the box of each
/// of its characters. Positions and boxes are from the piece's left edge.</summary>
internal sealed class ShapedRun(ushort[] glyphs, SKPoint[] positions, float width, float[] characterLefts, float[] characterRights)
{
    public ushort[] Glyphs { get; } = glyphs;

    public SKPoint[] Positions { get; } = positions;

    public float Width { get; } = width;

    /// <summary>Indexed from the piece's first character, in logical order.</summary>
    public float[] CharacterLefts { get; } = characterLefts;

    public float[] CharacterRights { get; } = characterRights;
}

/// <summary>
/// Text shaping with HarfBuzz: the glyphs a piece of text in one font and one direction
/// is drawn with — joined Arabic letters, Indic vowel signs in their place, marks on their
/// letters, emoji sequences composed into one picture.
/// </summary>
/// <remarks>
/// <para>
/// The HarfBuzz font is made from the typeface the way <c>SKShaper</c> makes it, and the
/// buffer is shaped here directly: the clusters and the advances are needed, not only
/// the positions SKShaper returns, to know where each character's box is.
/// </para>
/// <para>
/// The piece is added with the whole line as its context: the letters around a piece
/// cut off by a change of font still decide the forms of the letters at its ends.
/// </para>
/// <para>
/// Per thread, as the rest of the font caches: a HarfBuzz font is used by one thread
/// at a time, together with the SKFont of its line.
/// </para>
/// </remarks>
internal static class SkiaShaper
{
    /// <summary>The scale HarfBuzz works in, as SKShaper sets it.</summary>
    private const int Scale = 512;

    [ThreadStatic] private static Dictionary<SKTypeface, HbFont?>? t_fonts;

    /// <summary>Release this thread's HarfBuzz fonts: the font selection changed.</summary>
    public static void Clear()
    {
        if (t_fonts is null) return;

        foreach (HbFont? font in t_fonts.Values)
            font?.Dispose();

        t_fonts = null;
    }

    /// <summary>Shape a piece of a line. Null: the typeface gives HarfBuzz nothing to read,
    /// and the piece is drawn a glyph per character.</summary>
    /// <param name="text">The whole line, the context of the piece.</param>
    /// <param name="start">The piece's first index.</param>
    /// <param name="length">Its length.</param>
    /// <param name="font">The font it is drawn in.</param>
    /// <param name="rightToLeft">Its direction.</param>
    public static ShapedRun? Shape(string text, int start, int length, SKFont font, bool rightToLeft)
    {
        if (length == 0 || FontFor(font.Typeface) is not { } shaper) return null;

        using var buffer = new HbBuffer();
        buffer.AddUtf16(text, start, length);
        buffer.Direction = rightToLeft ? Direction.RightToLeft : Direction.LeftToRight;
        buffer.GuessSegmentProperties();

        shaper.Shape(buffer);

        GlyphInfo[] infos = buffer.GlyphInfos;
        GlyphPosition[] positions = buffer.GlyphPositions;

        float scaleY = font.Size / Scale;
        float scaleX = scaleY * font.ScaleX;

        int count = infos.Length;
        var glyphs = new ushort[count];
        var points = new SKPoint[count];

        // the extent of every cluster, by the index of its first character in the piece
        var clusterLeft = new float[length];
        var clusterRight = new float[length];
        var startsCluster = new bool[length];

        Array.Fill(clusterLeft, float.MaxValue);
        Array.Fill(clusterRight, float.MinValue);

        float pen = 0;

        for (int i = 0; i < count; i++)
        {
            glyphs[i] = (ushort)infos[i].Codepoint;

            // HarfBuzz's y grows up, Skia's down
            points[i] = new SKPoint(pen + positions[i].XOffset * scaleX, -positions[i].YOffset * scaleY);

            float advance = positions[i].XAdvance * scaleX;

            // the clusters are indices in the whole line
            int cluster = Math.Clamp((int)infos[i].Cluster - start, 0, length - 1);

            startsCluster[cluster] = true;
            clusterLeft[cluster] = Math.Min(clusterLeft[cluster], pen);
            clusterRight[cluster] = Math.Max(clusterRight[cluster], pen + advance);

            pen += advance;
        }

        var lefts = new float[length];
        var rights = new float[length];

        // a cluster spans from its first character to the next cluster's first; its box
        // is shared out among its characters — a ligature of "fi" gives each letter half
        int c = 0;

        while (c < length)
        {
            int next = c + 1;
            while (next < length && !startsCluster[next]) next++;

            float left = clusterLeft[c], right = clusterRight[c];

            // characters that made no glyph of their own: a zero-width box beside the
            // previous one
            if (left > right)
            {
                float at = c == 0
                    ? (rightToLeft ? pen : 0)
                    : (rightToLeft ? lefts[c - 1] : rights[c - 1]);

                left = right = at;
            }

            int count2 = next - c;

            for (int k = 0; k < count2; k++)
            {
                // the first character of a right-to-left cluster takes its right end
                int slot = rightToLeft ? count2 - 1 - k : k;

                lefts[c + k] = left + (right - left) * slot / count2;
                rights[c + k] = left + (right - left) * (slot + 1) / count2;
            }

            c = next;
        }

        return new ShapedRun(glyphs, points, pen, lefts, rights);
    }

    /// <summary>The HarfBuzz font of a typeface, made once per thread. Null: the typeface
    /// has no data stream to read its tables from.</summary>
    private static HbFont? FontFor(SKTypeface typeface)
    {
        Dictionary<SKTypeface, HbFont?> fonts = t_fonts ??= new Dictionary<SKTypeface, HbFont?>(ReferenceEqualityComparer.Instance);

        if (fonts.TryGetValue(typeface, out HbFont? cached))
            return cached;

        HbFont? created = null;

        // as SKShaper does it: the blob takes the stream over and releases it itself
        SKStreamAsset? stream = typeface.OpenStream(out int index);

        if (stream is not null)
        {
            using Blob blob = stream.ToHarfBuzzBlob();
            using var face = new Face(blob, index);

            face.Index = index;
            face.UnitsPerEm = typeface.UnitsPerEm;

            created = new HbFont(face);
            created.SetScale(Scale, Scale);
            created.SetFunctionsOpenType();
        }

        fonts[typeface] = created;
        return created;
    }
}
