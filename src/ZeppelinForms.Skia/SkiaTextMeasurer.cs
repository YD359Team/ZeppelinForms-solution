using SkiaSharp;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Skia;

public sealed class SkiaTextMeasurer : ITextMeasurer
{
    public static void Register() => TextMeasurer.Current = new SkiaTextMeasurer();

    public Size MeasureText(string text, Font font)
    {
        if (string.IsNullOrEmpty(text))
            return Size.Empty;

        float width = 0;
        float height = 0;

        foreach ((string run, SKFont runFont) in SkiaFontCache.SplitRuns(text, font))
        {
            width += runFont.MeasureText(run, out SKRect bounds);
            height = Math.Max(height, bounds.Height);
        }

        return new Size(width, height);
    }

    public float MeasureTextWidth(string text, int length, Font font)
    {
        if (length <= 0 || string.IsNullOrEmpty(text))
            return 0;

        length = Math.Min(length, text.Length);
        return MeasureText(text[..length], font).Width;
    }

    public Size MeasureRuns(IReadOnlyList<TextRun> runs, Font baseFont)
    {
        float width = 0;
        float ascent = 0, descent = 0;

        foreach (TextRun run in runs)
        {
            Font font = run.Font ?? baseFont;
            SKFont skFont = SkiaFontCache.Get(font);

            foreach ((string piece, SKFont pieceFont) in SkiaFontCache.SplitRuns(run.Text, font))
                width += pieceFont.MeasureText(piece);

            SKFontMetrics metrics = skFont.Metrics;
            ascent = Math.Max(ascent, -metrics.Ascent);
            descent = Math.Max(descent, metrics.Descent);
        }

        return new Size(width, ascent + descent);
    }
}