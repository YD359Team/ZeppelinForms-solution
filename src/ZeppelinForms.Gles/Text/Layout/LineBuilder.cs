using System.Globalization;
using System.Text;
using ZeppelinForms.Core.Text;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Gles.Text.Fonts;
using ZeppelinForms.Gles.Text.Shaping;

namespace ZeppelinForms.Gles.Text.Layout;

/// <summary>
/// A line of text into the glyphs it is drawn with — the same steps Skia's backend takes,
/// so a form lays out alike on both: split by font where the primary one lacks a character,
/// ordered by the Unicode Bidirectional Algorithm where the line needs it, shaped by
/// HarfBuzz where its script does.
/// </summary>
/// <remarks>
/// <para>
/// A line of Latin, Cyrillic, Greek or CJK in a left-to-right paragraph is laid out a glyph
/// per character at the font's advances, without kerning, as Skia's measure does; a line of
/// a complex script, emoji sequences or any right-to-left text goes through the shaper.
/// </para>
/// <para>
/// Lines are cached by their text, font and direction, on each thread: layout and drawing
/// ask about the same line many times a frame.
/// </para>
/// </remarks>
internal static class LineBuilder
{
    private const int CacheLimit = 4096;

    [ThreadStatic] private static Dictionary<(string Text, Font Font, bool RightToLeft), TextLine>? t_lines;

    [ThreadStatic] private static int t_version;
    private static int s_version;

    /// <summary>Forget every line: the fonts changed.</summary>
    public static void Invalidate()
    {
        FontResolver.Invalidate();
        Interlocked.Increment(ref s_version);
    }

    public static TextLine Get(string text, Font font, bool rightToLeft = false)
    {
        if (string.IsNullOrEmpty(text)) return TextLine.Empty;

        int version = Volatile.Read(ref s_version);

        if (t_lines is null || t_version != version)
        {
            t_lines = [];
            t_version = version;
        }

        var key = (text, font, rightToLeft);

        if (t_lines.TryGetValue(key, out TextLine? cached)) return cached;

        TextLine line = Build(WithoutTabs(text), font, rightToLeft);

        // a field typed into makes a line a keystroke: the cache starts over when full
        if (t_lines.Count >= CacheLimit) t_lines.Clear();

        t_lines[key] = line;
        return line;
    }

    /// <summary>A tab is laid out and drawn as a space, as in Skia's backend: fonts have
    /// no glyph for it, and the length stays, so every index of the text is the same.</summary>
    private static string WithoutTabs(string text) =>
        text.Contains('\t') ? text.Replace('\t', ' ') : text;

    private static TextLine Build(string text, Font font, bool rightToLeft)
    {
        Typeface? primary = FontResolver.Resolve(font);

        // a machine without a single font: nothing to draw, nothing to measure
        if (primary is null) return TextLine.Empty;

        bool complex = ComplexScripts.NeedsShaping(text);
        var runs = new List<GlyphRun>();

        if (!rightToLeft && !complex)
        {
            foreach ((int start, int length, Typeface typeface) in SplitByFont(text, 0, text.Length, font, primary))
                runs.Add(Plain(text, start, length, typeface, font.Size, rightToLeft: false));
        }
        else
        {
            BidiParagraph paragraph = BidiParagraph.Analyze(
                text, rightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight);

            foreach (BidiRun bidi in paragraph.LineRuns(0, text.Length))
            {
                var pieces = SplitByFont(text, bidi.Start, bidi.Start + bidi.Length, font, primary).ToList();

                // the pieces of a right-to-left run stand right to left as well
                if (bidi.IsRightToLeft) pieces.Reverse();

                foreach ((int start, int length, Typeface typeface) in pieces)
                {
                    // a right-to-left piece is always shaped: its glyphs are ordered and its
                    // brackets mirrored by the shaper
                    bool shape = (complex || bidi.IsRightToLeft) && HarfBuzz.IsAvailable;

                    runs.Add(shape
                        ? Shaped(text, start, length, typeface, font.Size, bidi.IsRightToLeft)
                        : Plain(text, start, length, typeface, font.Size, bidi.IsRightToLeft));
                }
            }
        }

        float width = 0;
        float left = float.MaxValue, top = float.MaxValue, right = float.MinValue, bottom = float.MinValue;

        foreach (GlyphRun run in runs)
        {
            Rectangle ink = InkOf(run, width);

            if (ink.Width > 0 || ink.Height > 0)
            {
                left = MathF.Min(left, ink.X);
                top = MathF.Min(top, ink.Y);
                right = MathF.Max(right, ink.Right);
                bottom = MathF.Max(bottom, ink.Bottom);
            }

            width += run.Width;
        }

        return new TextLine
        {
            Text = text,
            Runs = [.. runs],
            Width = width,
            InkBounds = left <= right ? new Rectangle(left, top, right - left, bottom - top) : Rectangle.Empty,
        };
    }

    /// <summary>The pieces of a part of the line covered by one typeface each: the primary
    /// one where it has the character, a fallback where it doesn't.</summary>
    /// <remarks>The unit is a grapheme cluster, chosen by its first character: an emoji of
    /// several code points glued by joiners must go to one font whole, though its parts may
    /// exist in different ones — as in Skia's backend.</remarks>
    private static IEnumerable<(int Start, int Length, Typeface Typeface)> SplitByFont(
        string text, int from, int end, Font font, Typeface primary)
    {
        int start = from;
        int position = from;
        Typeface? current = null;

        while (position < end)
        {
            int cluster = StringInfo.GetNextTextElementLength(text.AsSpan(position, end - position));
            if (cluster <= 0) cluster = 1;

            int codepoint = Rune.TryGetRuneAt(text, position, out Rune rune) ? rune.Value : text[position];

            // a cluster asking for emoji presentation (U+FE0F) — ❤️ — goes to an emoji font
            // even where the primary one has the plain character
            bool emoji = FontResolver.IsEmoji(codepoint) && text.AsSpan(position, cluster).Contains('️');

            Typeface typeface = !emoji && Covers(primary, codepoint)
                ? primary
                : FontResolver.Fallback(font, codepoint) ?? primary;

            if (current is null)
            {
                current = typeface;
            }
            else if (!ReferenceEquals(typeface, current))
            {
                yield return (start, position - start, current);

                start = position;
                current = typeface;
            }

            position += cluster;
        }

        if (current is not null && start < end)
            yield return (start, end - start, current);
    }

    /// <summary>Whether a typeface draws a character: a control or a space of no glyph
    /// is covered by any, so a line of them doesn't go looking for fallbacks.</summary>
    private static bool Covers(Typeface typeface, int codepoint) =>
        codepoint < 0x20 || typeface.Face.GlyphFor(codepoint) != 0;

    /// <summary>A glyph per character at its advance, left to right; for a right-to-left
    /// piece without a shaper, at least in its visual order.</summary>
    private static GlyphRun Plain(string text, int start, int length, Typeface typeface, float size, bool rightToLeft)
    {
        float scale = typeface.Scale(size);

        var glyphs = new List<ushort>(length);
        var advances = new List<float>(length);
        var lefts = new float[length];
        var rights = new float[length];

        float pen = 0;
        int index = start;
        int end = start + length;

        while (index < end)
        {
            int units = char.IsHighSurrogate(text[index]) && index + 1 < end && char.IsLowSurrogate(text[index + 1]) ? 2 : 1;
            int codepoint = units == 2 ? char.ConvertToUtf32(text[index], text[index + 1]) : text[index];

            ushort glyph = typeface.Face.GlyphFor(codepoint);
            float advance = typeface.Face.Advance(glyph) * scale;

            glyphs.Add(glyph);
            advances.Add(advance);

            for (int u = 0; u < units; u++)
            {
                lefts[index - start + u] = pen + advance * u / units;
                rights[index - start + u] = pen + advance * (u + 1) / units;
            }

            pen += advance;
            index += units;
        }

        int count = glyphs.Count;
        var xs = new float[count];
        var ys = new float[count];

        float x = 0;

        if (rightToLeft)
        {
            // the last character leftmost: the glyphs and the boxes are mirrored in the piece
            glyphs.Reverse();
            advances.Reverse();

            for (int k = 0; k < length; k++)
                (lefts[k], rights[k]) = (pen - rights[k], pen - lefts[k]);
        }

        for (int i = 0; i < count; i++)
        {
            xs[i] = x;
            x += advances[i];
        }

        return new GlyphRun
        {
            Start = start,
            Length = length,
            Typeface = typeface,
            Size = size,
            IsRightToLeft = rightToLeft,
            Glyphs = [.. glyphs],
            X = xs,
            Y = ys,
            Width = pen,
            CharacterLefts = lefts,
            CharacterRights = rights,
        };
    }

    /// <summary>A piece shaped by HarfBuzz, and the box of each of its characters from the
    /// clusters: a cluster spans from its first character to the next cluster's, and its
    /// box is shared out among its characters — a ligature of "fi" gives each letter half.</summary>
    private static GlyphRun Shaped(string text, int start, int length, Typeface typeface, float size, bool rightToLeft)
    {
        (ushort[] glyphs, int[] advances, int[] offsetsX, int[] offsetsY, int[] clusters) =
            HarfBuzz.Shape(typeface.Face, text, start, length, rightToLeft);

        float scale = typeface.Scale(size);
        int count = glyphs.Length;

        var xs = new float[count];
        var ys = new float[count];

        var clusterLeft = new float[length];
        var clusterRight = new float[length];
        var startsCluster = new bool[length];

        Array.Fill(clusterLeft, float.MaxValue);
        Array.Fill(clusterRight, float.MinValue);

        float pen = 0;

        for (int i = 0; i < count; i++)
        {
            xs[i] = pen + offsetsX[i] * scale;

            // HarfBuzz's y grows up, the screen's down
            ys[i] = -offsetsY[i] * scale;

            float advance = advances[i] * scale;
            int cluster = Math.Clamp(clusters[i] - start, 0, length - 1);

            startsCluster[cluster] = true;
            clusterLeft[cluster] = MathF.Min(clusterLeft[cluster], pen);
            clusterRight[cluster] = MathF.Max(clusterRight[cluster], pen + advance);

            pen += advance;
        }

        var lefts = new float[length];
        var rights = new float[length];

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

            int span = next - c;

            for (int k = 0; k < span; k++)
            {
                // the first character of a right-to-left cluster takes its right end
                int slot = rightToLeft ? span - 1 - k : k;

                lefts[c + k] = left + (right - left) * slot / span;
                rights[c + k] = left + (right - left) * (slot + 1) / span;
            }

            c = next;
        }

        return new GlyphRun
        {
            Start = start,
            Length = length,
            Typeface = typeface,
            Size = size,
            IsRightToLeft = rightToLeft,
            IsShaped = true,
            Glyphs = glyphs,
            X = xs,
            Y = ys,
            Width = pen,
            CharacterLefts = lefts,
            CharacterRights = rights,
        };
    }

    /// <summary>The ink of a piece standing at x on the line: its glyphs' boxes, y down
    /// from the baseline; a synthesized bold reaches half its widening further.</summary>
    private static Rectangle InkOf(GlyphRun run, float x)
    {
        float scale = run.Typeface.Scale(run.Size);
        float grow = run.Typeface.Embolden ? run.Size * Typeface.EmboldenStrength / 2f : 0;

        float left = float.MaxValue, top = float.MaxValue, right = float.MinValue, bottom = float.MinValue;

        for (int i = 0; i < run.Glyphs.Length; i++)
        {
            Rectangle b = run.Typeface.Face.Bounds(run.Glyphs[i]);
            if (b.Width <= 0 && b.Height <= 0) continue;

            float gx = x + run.X[i];
            float gy = run.Y[i];

            float x0 = gx + b.X * scale - grow;
            float x1 = gx + b.Right * scale + grow;
            float y0 = gy - b.Bottom * scale - grow;
            float y1 = gy - b.Y * scale + grow;

            // a slanted glyph leans right by a quarter of its height above the baseline
            if (run.Typeface.Oblique)
            {
                x0 += Typeface.ObliqueSkew * MathF.Min(0, -y1);
                x1 += Typeface.ObliqueSkew * MathF.Max(0, -y0);
            }

            left = MathF.Min(left, x0);
            top = MathF.Min(top, y0);
            right = MathF.Max(right, x1);
            bottom = MathF.Max(bottom, y1);
        }

        return left <= right ? new Rectangle(left, top, right - left, bottom - top) : Rectangle.Empty;
    }
}