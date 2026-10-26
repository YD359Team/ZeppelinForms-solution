using System.Collections.Concurrent;
using ZeppelinForms.Core.Text;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Gles.Geometry;
using ZeppelinForms.Gles.Imaging;
using ZeppelinForms.Gles.Text;
using ZeppelinForms.Gles.Text.Fonts;
using ZeppelinForms.Gles.Text.Layout;

namespace ZeppelinForms.Gles.Rendering;

/// <summary>
/// The text of <see cref="GlesGraphics"/>: lines laid out by <see cref="LineBuilder"/> —
/// the measurer's very lines — drawn glyph by glyph from the glyph atlas.
/// </summary>
/// <remarks>
/// <para>
/// Under a scale and an offset, the usual case, a glyph is drawn from the atlas at its
/// device size, its pen on a quarter of a pixel across and the baseline on a whole pixel
/// down: sharp, and one quad a glyph in the frame's batch. Under a rotation or a skew, and
/// at sizes no atlas holds, the glyphs are filled as paths instead, exact at any angle.
/// </para>
/// <para>
/// Emoji of a color font are pictures: drawn as images, scaled down from the font's strike.
/// The outline of <see cref="Graphics.TextEffects"/> is the glyphs' outlines stroked under
/// the fill, the decorations are bars at the heights of Skia's backend.
/// </para>
/// </remarks>
internal sealed partial class GlesGraphics
{
    /// <summary>Above this many device pixels a glyph is filled as a path, not cached.</summary>
    private const float MaxAtlasGlyphSize = 400f;

    private static readonly ConcurrentDictionary<(int Face, ushort Glyph), ColorGlyphPicture?> ColorGlyphs = new();

    /// <summary>An emoji's picture decoded, and the strike's metrics it stands by.</summary>
    private sealed record ColorGlyphPicture(Image Image, Text.OpenType.ColorBitmap Bitmap);

    private readonly Polylines _glyphLines = new();

    /// <summary>The text drawn now is in a right-to-left paragraph: its line is ordered so.</summary>
    private bool IsRightToLeft => TextDirection == FlowDirection.RightToLeft;

    public override void DrawText(string text, Point position, Color color, Font font)
    {
        if (string.IsNullOrEmpty(text)) return;

        TextLine line = LineBuilder.Get(text, font, IsRightToLeft);

        // the outline goes under the glyphs: the fill then covers its inner half
        DrawOutline(line, position.X, position.Y);
        DrawLine(line, position.X, position.Y, color);
        DrawDecorations(TextEffects.Decorations, position.X, line.Width, position.Y, font, color);
    }

    public override void DrawText(
        string text, Rectangle rect, Color color, Font font,
        HorizontalContentAlignment hAlign = HorizontalContentAlignment.Center,
        VerticalContentAlignment vAlign = VerticalContentAlignment.Center)
    {
        if (string.IsNullOrEmpty(text)) return;

        TextLine line = LineBuilder.Get(text, font, IsRightToLeft);

        // the line box of the font, not the ink of these letters: every text of the font
        // in an equal rectangle stands on one baseline
        FontMetrics box = TextMeasurer.Current.MeasureFont(font);

        DrawPlacedLine(line, rect, color, font, hAlign, box.Baseline(rect.Y, rect.Height, vAlign));
    }

    /// <summary>Centered by its ink: an arrow, a number in a circle — alone in its shape,
    /// it must look centered there, whatever the font's line says.</summary>
    public override void DrawSymbol(
        string text, Rectangle rect, Color color, Font font,
        HorizontalContentAlignment hAlign = HorizontalContentAlignment.Center,
        VerticalContentAlignment vAlign = VerticalContentAlignment.Center)
    {
        if (string.IsNullOrEmpty(text)) return;

        TextLine line = LineBuilder.Get(text, font, IsRightToLeft);
        Rectangle ink = line.InkBounds;

        float baseline = vAlign switch
        {
            VerticalContentAlignment.Top => rect.Y - ink.Y,
            VerticalContentAlignment.Bottom => rect.Y + rect.Height - ink.Bottom,
            _ => rect.Y + rect.Height / 2f - (ink.Y + ink.Height / 2f),
        };

        DrawPlacedLine(line, rect, color, font, hAlign, baseline);
    }

    public override void DrawRuns(
        IReadOnlyList<TextRun> runs, Rectangle rect, Font baseFont, Color baseColor,
        HorizontalContentAlignment hAlign = HorizontalContentAlignment.Center,
        VerticalContentAlignment vAlign = VerticalContentAlignment.Center)
    {
        if (runs.Count == 0) return;

        float totalWidth = 0;
        float maxAscent = 0, maxDescent = 0;

        // the first pass — the extents, to know where to start from
        foreach (TextRun run in runs)
        {
            Font font = run.Font ?? baseFont;

            totalWidth += LineBuilder.Get(run.Text, font, IsRightToLeft).Width;

            FontMetrics metrics = TextMeasurer.Current.MeasureFont(font);
            maxAscent = MathF.Max(maxAscent, metrics.Ascent);
            maxDescent = MathF.Max(maxDescent, metrics.Descent);
        }

        float lineHeight = maxAscent + maxDescent;

        float x = hAlign switch
        {
            HorizontalContentAlignment.Left => rect.X,
            HorizontalContentAlignment.Right => rect.X + rect.Width - totalWidth,
            _ => rect.X + (rect.Width - totalWidth) / 2f,
        };

        float top = vAlign switch
        {
            VerticalContentAlignment.Top => rect.Y,
            VerticalContentAlignment.Bottom => rect.Y + rect.Height - lineHeight,
            _ => rect.Y + (rect.Height - lineHeight) / 2f,
        };

        // a shared baseline: runs of different sizes stand on one line
        float baseline = top + maxAscent;

        foreach (TextRun run in runs)
        {
            Font font = run.Font ?? baseFont;
            Color color = run.Color ?? baseColor;

            TextLine line = LineBuilder.Get(run.Text, font, IsRightToLeft);

            if (run.Background is Color background)
                FillRectangle(new Rectangle(x, top, line.Width, lineHeight), background);

            DrawOutline(line, x, baseline);
            DrawLine(line, x, baseline, color);

            // the run's own lines add to the element's
            TextDecorations decorations = TextEffects.Decorations;

            if (run.Underline) decorations |= TextDecorations.Underline;
            if (run.Strikethrough) decorations |= TextDecorations.Strikethrough;

            DrawDecorations(decorations, x, line.Width, baseline, font, color);

            x += line.Width;
        }
    }

    /// <summary>A line on a baseline already chosen, aligned across in the rectangle and cut
    /// off at it when it doesn't fit — a clip only then, it costs a save and a restore.</summary>
    private void DrawPlacedLine(
        TextLine line, Rectangle rect, Color color, Font font, HorizontalContentAlignment hAlign, float baseline)
    {
        float width = line.Width;

        float x = hAlign switch
        {
            HorizontalContentAlignment.Left => rect.X,
            HorizontalContentAlignment.Right => rect.X + rect.Width - width,
            _ => rect.X + (rect.Width - width) / 2f,
        };

        bool clipped = width > rect.Width;

        if (clipped)
        {
            Save();
            ClipRect(rect);
        }

        DrawOutline(line, x, baseline);
        DrawLine(line, x, baseline, color);
        DrawDecorations(TextEffects.Decorations, x, width, baseline, font, color);

        if (clipped) Restore();
    }

    /// <summary>The glyphs of a line, its pieces one after another from the left.</summary>
    private void DrawLine(TextLine line, float x, float baseline, Color color)
    {
        if (color.A == 0 || line.Runs.Length == 0) return;

        uint packed = Premultiply(color);

        foreach (GlyphRun run in line.Runs)
        {
            DrawGlyphRun(run, x, baseline, packed);
            x += run.Width;
        }
    }

    private void DrawGlyphRun(GlyphRun run, float x, float baseline, uint packed)
    {
        Affine m = _state.Matrix;
        Typeface typeface = run.Typeface;

        bool atlas = m.IsPositiveScaleTranslate && MathF.Abs(m.A - m.D) < 1e-3f * m.A && run.Size * m.A <= MaxAtlasGlyphSize;

        if (!atlas)
        {
            FillGlyphPaths(run, x, baseline, packed);
            return;
        }

        float scale = m.A;
        float size = run.Size * scale;
        float kind = Shaders.Kind(Shaders.ShapeNone, Shaders.PaintGlyph);

        // the baseline on a whole pixel: the stems of every line land the same way
        float deviceBaseline = MathF.Round(m.D * baseline + m.F);

        for (int i = 0; i < run.Glyphs.Length; i++)
        {
            ushort glyph = run.Glyphs[i];

            if (typeface.Face.HasColorBitmaps && ColorGlyph(typeface, glyph) is { } picture)
            {
                DrawColorGlyph(run, picture, x + run.X[i], baseline + run.Y[i]);
                continue;
            }

            float deviceX = m.A * (x + run.X[i]) + m.E;
            int py = (int)MathF.Round(deviceBaseline + run.Y[i] * scale);
            int px = (int)MathF.Floor(deviceX);
            int subpixel = Math.Clamp((int)((deviceX - px) * GlyphRasterizer.SubpixelSteps), 0, GlyphRasterizer.SubpixelSteps - 1);

            GlyphSlot slot = _renderer.Glyph(typeface, glyph, size, subpixel);
            if (slot.IsEmpty) continue;

            var device = new Rectangle(px + slot.Left, py + slot.Top, slot.Width, slot.Height);
            Span<Vertex> quad = Quad(device);
            if (quad.IsEmpty) continue;

            float x0 = device.X, y0 = device.Y, x1 = device.Right, y1 = device.Bottom;

            quad[0] = DeviceVertex(x0, y0, packed, kind, slot.U0, slot.V0);
            quad[1] = DeviceVertex(x1, y0, packed, kind, slot.U1, slot.V0);
            quad[2] = DeviceVertex(x1, y1, packed, kind, slot.U1, slot.V1);
            quad[3] = DeviceVertex(x0, y1, packed, kind, slot.U0, slot.V1);
        }
    }

    /// <summary>A run's glyphs filled as one path: under a rotation, a skew or at a size
    /// no atlas holds.</summary>
    private void FillGlyphPaths(GlyphRun run, float x, float baseline, uint packed)
    {
        Typeface typeface = run.Typeface;

        _glyphLines.Clear();
        AddGlyphOutlines(run, x, baseline, _glyphLines, out bool anyColor);

        if (_glyphLines.PointCount > 0)
        {
            if (typeface.Embolden)
            {
                // the widening is a stroke around the outline, in the line's own units
                _outline.Clear();
                _stroker.Stroke(_glyphLines, run.Size * Typeface.EmboldenStrength, LineCap.Butt, LineJoin.Round,
                    Stroker.DefaultMiterLimit, FlattenTolerance / _state.Matrix.MeanScale, _outline);
                _outline.Transform(_state.Matrix);

                FillDevice(_outline, packed, FillRule.NonZero);
            }

            _glyphLines.Transform(_state.Matrix);
            FillDevice(_glyphLines, packed, FillRule.NonZero);
        }

        if (!anyColor) return;

        for (int i = 0; i < run.Glyphs.Length; i++)
        {
            if (ColorGlyph(typeface, run.Glyphs[i]) is { } picture)
                DrawColorGlyph(run, picture, x + run.X[i], baseline + run.Y[i]);
        }
    }

    /// <summary>The outlines of a run's glyphs in the line's units, placed at their pens.</summary>
    private static void AddGlyphOutlines(GlyphRun run, float x, float baseline, Polylines output, out bool anyColor)
    {
        Typeface typeface = run.Typeface;
        float scale = typeface.Scale(run.Size);

        anyColor = false;

        for (int i = 0; i < run.Glyphs.Length; i++)
        {
            ushort glyph = run.Glyphs[i];

            if (typeface.Face.HasColorBitmaps && ColorGlyph(typeface, glyph) is not null)
            {
                anyColor = true;
                continue;
            }

            if (typeface.Face.Outline(glyph) is not { } outline) continue;

            var matrix = new Affine(
                scale, 0,
                typeface.Oblique ? Typeface.ObliqueSkew * scale : 0, -scale,
                x + run.X[i], baseline + run.Y[i]);

            outline.Flatten(matrix, FlattenTolerance / 4f, output);
        }
    }

    /// <summary>The outline of <see cref="Graphics.TextEffects"/>: the glyphs stroked twice as
    /// wide as the outline reaches out, round at the corners — a miter spikes out of A and V.</summary>
    private void DrawOutline(TextLine line, float x, float baseline)
    {
        TextEffects effects = TextEffects;

        if (!effects.HasOutline || line.Runs.Length == 0) return;

        _glyphLines.Clear();

        foreach (GlyphRun run in line.Runs)
        {
            AddGlyphOutlines(run, x, baseline, _glyphLines, out _);
            x += run.Width;
        }

        if (_glyphLines.PointCount == 0) return;

        StrokeLocal(_glyphLines, effects.OutlineWidth * 2f, LineCap.Round, LineJoin.Round, Premultiply(effects.OutlineColor));
    }

    /// <summary>Decoration lines along a piece of text, at the heights of the underline and
    /// the strikethrough of <see cref="TextRun"/>, as Skia's backend draws them.</summary>
    private void DrawDecorations(TextDecorations decorations, float x, float width, float baseline, Font font, Color textColor)
    {
        if (decorations == TextDecorations.None || width <= 0f) return;

        Color color = TextEffects.LineColor(textColor);
        float thickness = MathF.Max(1f, font.Size / 14f);

        void Bar(float y) => FillRectangle(new Rectangle(x, y - thickness / 2f, width, thickness), color);

        if ((decorations & TextDecorations.Underline) != 0) Bar(baseline + font.Size * 0.12f);
        if ((decorations & TextDecorations.Strikethrough) != 0) Bar(baseline - font.Size * 0.28f);

        // up from the baseline to the top of the capitals
        if ((decorations & TextDecorations.Overline) != 0)
            Bar(baseline - TextMeasurer.Current.MeasureFont(font).Ascent * 0.92f);
    }

    #region Color glyphs

    /// <summary>An emoji's picture, decoded once for the process.</summary>
    private static ColorGlyphPicture? ColorGlyph(Typeface typeface, ushort glyph) =>
        ColorGlyphs.GetOrAdd((typeface.Face.Id, glyph), static (key, face) =>
        {
            if (face.ColorBitmap(key.Glyph) is not { } bitmap) return null;

            try
            {
                (int width, int height, byte[] pixels) = PngDecoder.Decode(bitmap.Png);
                return new ColorGlyphPicture(new Image(width, height, pixels), bitmap);
            }
            catch (InvalidDataException)
            {
                return null;
            }
        }, typeface.Face);

    /// <summary>An emoji at its pen: the strike's picture scaled to the font's size, standing
    /// on the baseline by the strike's bearings. Its color is its own.</summary>
    private void DrawColorGlyph(GlyphRun run, ColorGlyphPicture picture, float penX, float baseline)
    {
        Text.OpenType.ColorBitmap bitmap = picture.Bitmap;
        float scale = run.Size / bitmap.PixelsPerEm;

        var rect = new Rectangle(
            penX + bitmap.BearingX * scale,
            baseline - bitmap.BearingY * scale,
            bitmap.Width * scale,
            bitmap.Height * scale);

        DrawImage(rect, picture.Image);
    }

    #endregion
}