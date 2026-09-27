using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Headless;

/// <summary>
/// Computes text sizes by a fixed character width. The numbers are predictable
/// and the same on all machines — tests need reproducibility, not precision.
/// </summary>
public sealed class HeadlessTextMeasurer : ITextMeasurer
{
    /// <summary>Character width as a fraction of the font size.</summary>
    public float CharWidthRatio { get; set; } = 0.6f;

    /// <summary>Line height as a fraction of the font size.</summary>
    public float LineHeightRatio { get; set; } = 1.2f;

    public static void Register() => TextMeasurer.Current = new HeadlessTextMeasurer();

    public Size MeasureText(string text, Font font)
    {
        if (string.IsNullOrEmpty(text))
            return new Size(0, font.Size * LineHeightRatio);

        // multi-line text is measured by its longest line
        string[] lines = text.Split('\n');

        int widest = 0;
        foreach (string line in lines)
            widest = Math.Max(widest, line.Length);

        return new Size(
            widest * font.Size * CharWidthRatio,
            lines.Length * font.Size * LineHeightRatio);
    }

    public float MeasureTextWidth(string text, int length, Font font)
    {
        if (length <= 0 || string.IsNullOrEmpty(text))
            return 0;

        return Math.Min(length, text.Length) * font.Size * CharWidthRatio;
    }

    /// <summary>There are no fonts at all, so it is always ready: tests must not
    /// depend on loading that doesn't happen here.</summary>
    public bool IsReady(Font font) => true;

    public Task PrepareAsync(Font font) => Task.CompletedTask;

    public Size MeasureRuns(IReadOnlyList<TextRun> runs, Font baseFont)
    {
        float width = 0;
        float height = 0;

        foreach (TextRun run in runs)
        {
            Font font = run.Font ?? baseFont;
            width += run.Text.Length * font.Size * CharWidthRatio;
            height = Math.Max(height, font.Size * LineHeightRatio);
        }

        return new Size(width, height);
    }
}