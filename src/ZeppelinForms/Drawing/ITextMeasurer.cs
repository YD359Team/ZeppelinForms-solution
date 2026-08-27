using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Drawing;

public interface ITextMeasurer
{
    Size MeasureText(string text);
    float MeasureTextWidth(string text, int length);
}

public static class TextMeasurer
{
    public static ITextMeasurer Current { get; set; } = new NotRegisteredTextMeasurer();

    private sealed class NotRegisteredTextMeasurer : ITextMeasurer
    {
        public Size MeasureText(string text) =>
            throw new InvalidOperationException(
                "Text measurer не зарегистрирован. Вызовите SkiaTextMeasurer.Register().");

        public float MeasureTextWidth(string text, int length) =>
    throw new InvalidOperationException("Text measurer не зарегистрирован.");
    }
}

public class SkiaTextMeasurer : ITextMeasurer
{
    public Size MeasureText(string text)
    {
        throw new NotImplementedException();
    }

    public float MeasureTextWidth(string text, int length)
    {
        if (length <= 0 || string.IsNullOrEmpty(text)) return 0;
        length = Math.Min(length, text.Length);
        return Font.MeasureText(text.AsSpan(0, length));
    }
}
