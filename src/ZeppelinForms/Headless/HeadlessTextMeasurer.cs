using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Headless;

/// <summary>
/// Считает размеры текста по фиксированной ширине символа.
/// Числа предсказуемы и одинаковы на всех машинах — тестам нужна
/// воспроизводимость, а не точность.
/// </summary>
public sealed class HeadlessTextMeasurer : ITextMeasurer
{
    /// <summary>Ширина символа как доля от размера шрифта.</summary>
    public float CharWidthRatio { get; set; } = 0.6f;

    /// <summary>Высота строки как доля от размера шрифта.</summary>
    public float LineHeightRatio { get; set; } = 1.2f;

    public static void Register() => TextMeasurer.Current = new HeadlessTextMeasurer();

    public Size MeasureText(string text, Font font)
    {
        if (string.IsNullOrEmpty(text))
            return new Size(0, font.Size * LineHeightRatio);

        // многострочный текст меряем по самой длинной строке
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
