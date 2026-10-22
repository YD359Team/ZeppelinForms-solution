using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Gles.Geometry;
using ZeppelinForms.Gles.Text.Fonts;

namespace ZeppelinForms.Gles.Text;

/// <summary>A glyph's coverage at one size and one subpixel offset, and where its box stands
/// against the pen: <see cref="Left"/> and <see cref="Top"/> in whole pixels from the pen's
/// pixel on the baseline, y down.</summary>
internal readonly record struct GlyphBitmap(int Left, int Top, int Width, int Height, byte[]? Coverage)
{
    public static readonly GlyphBitmap Empty = new(0, 0, 0, 0, null);

    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>
/// Glyph outlines into antialiased coverage, unhinted, at a quarter of a pixel's offset
/// across: the exact-area rasterizer of the paths, with a synthesized bold as a stroke
/// around the outline and a synthesized italic as a slant of it.
/// </summary>
/// <remarks>
/// <para>
/// The coverage is lifted by a gamma of 1/1.3 before it is stored: linear coverage blended
/// in sRGB makes unhinted text look thin and grey at UI sizes, and every text renderer
/// corrects for it — Skia with its contrast hack, FreeType's users with a gamma table.
/// </para>
/// <para>
/// One instance per atlas, used from the thread that draws: its buffers are reused.
/// </para>
/// </remarks>
internal sealed class GlyphRasterizer
{
    /// <summary>The subpixel offsets across a pixel a glyph is drawn at.</summary>
    public const int SubpixelSteps = 4;

    private const float Tolerance = 0.15f;

    private static readonly byte[] Gamma = BuildGamma(1.3f);

    private readonly Rasterizer _rasterizer = new();
    private readonly Stroker _stroker = new();
    private readonly Polylines _lines = new();
    private readonly Polylines _outline = new();

    private static byte[] BuildGamma(float gamma)
    {
        var table = new byte[256];

        for (int i = 0; i < 256; i++)
            table[i] = (byte)MathF.Round(MathF.Pow(i / 255f, 1f / gamma) * 255f);

        return table;
    }

    /// <param name="size">The size in device pixels.</param>
    /// <param name="subpixel">The offset across, in quarters of a pixel.</param>
    public GlyphBitmap Rasterize(Typeface typeface, ushort glyph, float size, int subpixel)
    {
        VectorPath? outline = typeface.Face.Outline(glyph);
        if (outline is null) return GlyphBitmap.Empty;

        float scale = typeface.Scale(size);
        float offset = subpixel / (float)SubpixelSteps;

        // font units, y up, into pixels, y down; a slant leans the top right
        var matrix = new Affine(
            scale, 0,
            typeface.Oblique ? Typeface.ObliqueSkew * scale : 0, -scale,
            offset, 0);

        _lines.Clear();
        outline.Flatten(matrix, Tolerance, _lines);

        if (_lines.PointCount == 0) return GlyphBitmap.Empty;

        float grow = 0;

        if (typeface.Embolden)
        {
            float strength = size * Typeface.EmboldenStrength;
            grow = strength / 2f;

            _outline.Clear();
            _stroker.Stroke(_lines, strength, LineCap.Butt, LineJoin.Round, Stroker.DefaultMiterLimit, Tolerance, _outline);
        }

        Rectangle bounds = _lines.Bounds;

        int left = (int)MathF.Floor(bounds.X - grow) - 1;
        int top = (int)MathF.Floor(bounds.Y - grow) - 1;
        int right = (int)MathF.Ceiling(bounds.Right + grow) + 1;
        int bottom = (int)MathF.Ceiling(bounds.Bottom + grow) + 1;

        int width = right - left;
        int height = bottom - top;

        // a glyph larger than any atlas holds is drawn as a path instead
        if (width <= 0 || height <= 0 || width > 1024 || height > 1024) return GlyphBitmap.Empty;

        var coverage = new byte[width * height];
        _rasterizer.Rasterize(_lines, left, top, width, height, FillRule.NonZero, coverage);

        if (typeface.Embolden)
        {
            // the stroke is rasterized apart and joined by the larger coverage: its rings
            // wind against the outline's holes, and a sum would cut them out
            var stroke = new byte[width * height];
            _rasterizer.Rasterize(_outline, left, top, width, height, FillRule.NonZero, stroke);

            for (int i = 0; i < coverage.Length; i++)
                if (stroke[i] > coverage[i]) coverage[i] = stroke[i];
        }

        for (int i = 0; i < coverage.Length; i++)
            coverage[i] = Gamma[coverage[i]];

        return new GlyphBitmap(left, top, width, height, coverage);
    }
}