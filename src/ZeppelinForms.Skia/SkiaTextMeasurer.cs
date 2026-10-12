using SkiaSharp;
using ZeppelinForms.Core.Text;
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

    /// <summary>The line's boxes from the very pieces it is drawn with: shaped runs give
    /// them from HarfBuzz's clusters, plain ones from their characters' advances.</summary>
    public TextLineLayout LayOutLine(string text, Font font, FlowDirection direction)
    {
        ArgumentNullException.ThrowIfNull(text);

        bool rightToLeft = direction == FlowDirection.RightToLeft;

        if (text.Length == 0) return TextLineLayout.Empty(rightToLeft);

        CachedLine line = SkiaFontCache.GetLine(text, font, rightToLeft);

        return line.Layout ??= BuildLayout(line, rightToLeft);
    }

    private static TextLineLayout BuildLayout(CachedLine line, bool rightToLeft)
    {
        string text = line.Text;
        var lefts = new float[text.Length];
        var rights = new float[text.Length];
        var runs = new TextLineRun[line.Runs.Length];
        float x = 0;

        for (int i = 0; i < line.Runs.Length; i++)
        {
            FontRun run = line.Runs[i];

            if (run.Shaped is { } shaped)
            {
                for (int k = 0; k < run.Length; k++)
                {
                    lefts[run.Start + k] = x + shaped.CharacterLefts[k];
                    rights[run.Start + k] = x + shaped.CharacterRights[k];
                }
            }
            else
            {
                // a glyph per character at its advance, left to right — the sum of the
                // advances is the run's width, as the plain path draws it
                float position = 0;
                int index = run.Start;
                int end = run.Start + run.Length;

                while (index < end)
                {
                    int units = char.IsHighSurrogate(text[index]) && index + 1 < end ? 2 : 1;
                    float advance = run.Font.MeasureText(text.AsSpan(index, units));

                    for (int u = 0; u < units; u++)
                    {
                        lefts[index + u] = x + position + advance * u / units;
                        rights[index + u] = x + position + advance * (u + 1) / units;
                    }

                    position += advance;
                    index += units;
                }
            }

            runs[i] = new TextLineRun(run.Start, run.Length, run.IsRightToLeft, x, run.Width);
            x += run.Width;
        }

        return new TextLineLayout(text, rightToLeft, runs, lefts, rights);
    }

    /// <summary>On desktop platforms fonts are taken from the system
    /// synchronously — there is nothing to wait for.</summary>
    public bool IsReady(Font font) => true;

    public Task PrepareAsync(Font font) => Task.CompletedTask;

    public FontMetrics MeasureFont(Font font)
    {
        SKFontMetrics metrics = SkiaFontCache.Get(font).Metrics;

        // Skia's ascent is negative: up from the baseline
        return new FontMetrics(-metrics.Ascent, metrics.Descent);
    }

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