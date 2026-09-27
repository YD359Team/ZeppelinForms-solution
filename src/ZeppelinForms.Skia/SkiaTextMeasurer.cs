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

        CachedLine line = SkiaFontCache.GetLine(text, font);
        return new Size(line.Width, line.Height);
    }

    public float MeasureTextWidth(string text, int length, Font font)
    {
        if (length <= 0 || string.IsNullOrEmpty(text))
            return 0;

        length = Math.Min(length, text.Length);

        // the whole line is the already computed width of the line as a whole
        if (length == text.Length)
            return SkiaFontCache.GetLine(text, font).Width;

        return SkiaFontCache.MeasurePrefix(text, length, font);
    }

    /// <summary>On desktop platforms fonts are taken from the system
    /// synchronously — there is nothing to wait for.</summary>
    public bool IsReady(Font font) => true;

    public Task PrepareAsync(Font font) => Task.CompletedTask;

    public Size MeasureRuns(IReadOnlyList<TextRun> runs, Font baseFont)
    {
        float width = 0;
        float ascent = 0, descent = 0;

        foreach (TextRun run in runs)
        {
            Font font = run.Font ?? baseFont;

            width += SkiaFontCache.GetLine(run.Text, font).Width;

            SKFontMetrics metrics = SkiaFontCache.Get(font).Metrics;
            ascent = Math.Max(ascent, -metrics.Ascent);
            descent = Math.Max(descent, metrics.Descent);
        }

        return new Size(width, ascent + descent);
    }
}