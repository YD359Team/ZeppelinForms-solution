using ZeppelinForms.Core.Text;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Gles.Text.Fonts;
using ZeppelinForms.Gles.Text.Layout;

namespace ZeppelinForms.Gles.Text;

/// <summary>
/// Text measured by the GLES backend's own fonts: the same lines its graphics draw, so a
/// caret, a selection and a click land on the letters as they are on the screen.
/// </summary>
/// <remarks>The measures follow Skia's backend: a line's width is its advance, its height
/// the height of its ink, a font's ascent and descent those of its horizontal header.</remarks>
public sealed class GlesTextMeasurer : ITextMeasurer
{
    public static void Register() => TextMeasurer.Current = new GlesTextMeasurer();

    /// <summary>Forget the fonts chosen and the lines laid out: a font file was installed, or
    /// <see cref="FontFallbacks"/> changed in a way the next line must see at once.</summary>
    public static void InvalidateFonts() => LineBuilder.Invalidate();

    public Size MeasureText(string text, Font font)
    {
        if (string.IsNullOrEmpty(text)) return Size.Empty;

        TextLine line = LineBuilder.Get(text, font);
        return new Size(line.Width, line.InkHeight);
    }

    public float MeasureTextWidth(string text, int length, Font font)
    {
        if (length <= 0 || string.IsNullOrEmpty(text)) return 0;

        length = Math.Min(length, text.Length);

        TextLine line = LineBuilder.Get(text, font);

        if (length == text.Length) return line.Width;

        float width = 0;

        // the pieces are in visual order: those of the prefix may be anywhere among them
        foreach (GlyphRun run in line.Runs)
        {
            if (run.Start >= length) continue;

            int available = Math.Min(run.Length, length - run.Start);

            if (available == run.Length)
            {
                width += run.Width;
                continue;
            }

            for (int k = 0; k < available; k++)
                width += run.CharacterRights[k] - run.CharacterLefts[k];
        }

        return width;
    }

    public Size MeasureRuns(IReadOnlyList<TextRun> runs, Font baseFont)
    {
        float width = 0;
        float ascent = 0, descent = 0;

        foreach (TextRun run in runs)
        {
            Font font = run.Font ?? baseFont;

            width += LineBuilder.Get(run.Text, font).Width;

            FontMetrics metrics = MeasureFont(font);
            ascent = MathF.Max(ascent, metrics.Ascent);
            descent = MathF.Max(descent, metrics.Descent);
        }

        return new Size(width, ascent + descent);
    }

    public FontMetrics MeasureFont(Font font)
    {
        ArgumentNullException.ThrowIfNull(font);

        // without any font, a typical split of a line
        if (FontResolver.Resolve(font) is not { } typeface)
            return new FontMetrics(font.Size * 0.95f, font.Size * 0.25f);

        return new FontMetrics(typeface.Ascent(font.Size), typeface.Descent(font.Size));
    }

    /// <summary>The line's boxes from the very glyphs it is drawn with.</summary>
    public TextLineLayout LayOutLine(string text, Font font, FlowDirection direction)
    {
        ArgumentNullException.ThrowIfNull(text);

        bool rightToLeft = direction == FlowDirection.RightToLeft;

        if (text.Length == 0) return TextLineLayout.Empty(rightToLeft);

        TextLine line = LineBuilder.Get(text, font, rightToLeft);

        return line.Layout ??= BuildLayout(line, rightToLeft);
    }

    private static TextLineLayout BuildLayout(TextLine line, bool rightToLeft)
    {
        string text = line.Text;
        var lefts = new float[text.Length];
        var rights = new float[text.Length];
        var runs = new TextLineRun[line.Runs.Length];
        float x = 0;

        for (int i = 0; i < line.Runs.Length; i++)
        {
            GlyphRun run = line.Runs[i];

            for (int k = 0; k < run.Length; k++)
            {
                lefts[run.Start + k] = x + run.CharacterLefts[k];
                rights[run.Start + k] = x + run.CharacterRights[k];
            }

            runs[i] = new TextLineRun(run.Start, run.Length, run.IsRightToLeft, x, run.Width);
            x += run.Width;
        }

        return new TextLineLayout(text, rightToLeft, runs, lefts, rights);
    }

    /// <summary>The fonts are the system's, read when first asked for: always ready.</summary>
    public bool IsReady(Font font) => true;

    public Task PrepareAsync(Font font) => Task.CompletedTask;
}