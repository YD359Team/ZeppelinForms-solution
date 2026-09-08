using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Drawing;

public static class TextMeasurer
{
    public static ITextMeasurer Current { get; set; } = new NotRegisteredTextMeasurer();

    private sealed class NotRegisteredTextMeasurer : ITextMeasurer
    {
        public Size MeasureRuns(IReadOnlyList<TextRun> runs, Font baseFont) =>
            throw new InvalidOperationException(
                "Text measurer не зарегистрирован. Вызовите SkiaTextMeasurer.Register().");

        public Size MeasureText(string text, Font font) =>
            throw new InvalidOperationException(
                "Text measurer не зарегистрирован. Вызовите SkiaTextMeasurer.Register().");

        public float MeasureTextWidth(string text, int length, Font font) =>
            throw new InvalidOperationException(
                "Text measurer не зарегистрирован. Вызовите SkiaTextMeasurer.Register().");

        // не бросаем: это спрашивают перед измерением, чтобы решить —
        // ждать шрифт или рисовать запасным. Ответ «нет» безопаснее исключения
        public bool IsReady(Font font) => false;

        public Task PrepareAsync(Font font) => Task.CompletedTask;
    }
}