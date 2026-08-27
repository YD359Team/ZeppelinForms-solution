using SkiaSharp;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Skia;

public sealed class SkiaTextMeasurer : ITextMeasurer
{
    // тот же шрифт, что использует SkiaGraphics — иначе Measure и реальная
    // отрисовка разойдутся в оценке размера
    private static readonly SKFont Font = new(SKTypeface.Default, 16);

    public static void Register() => TextMeasurer.Current = new SkiaTextMeasurer();

    public Size MeasureText(string text)
    {
        float width = Font.MeasureText(text, out SKRect bounds);
        return new Size(width, bounds.Height);
    }

    public float MeasureTextWidth(string text, int length)
    {
        if (length <= 0 || string.IsNullOrEmpty(text))
            return 0;

        length = Math.Min(length, text.Length);
        return Font.MeasureText(text.AsSpan(0, length));
    }
}