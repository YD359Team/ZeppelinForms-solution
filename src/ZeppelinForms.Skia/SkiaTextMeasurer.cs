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
}