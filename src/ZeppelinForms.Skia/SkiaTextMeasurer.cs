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
        SKFont skFont = SkiaFontCache.Get(font);
        float width = skFont.MeasureText(text, out SKRect bounds);
        return new Size(width, bounds.Height);
    }

    public float MeasureTextWidth(string text, int length, Font font)
    {
        if (length <= 0 || string.IsNullOrEmpty(text))
            return 0;

        length = Math.Min(length, text.Length);
        return SkiaFontCache.Get(font).MeasureText(text.AsSpan(0, length));
    }
}