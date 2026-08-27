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
            throw new InvalidOperationException(
                "Text measurer не зарегистрирован. Вызовите SkiaTextMeasurer.Register().");
    }
}
