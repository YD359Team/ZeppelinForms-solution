using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Drawing;

public static class TextMeasurer
{
    public static ITextMeasurer Current { get; set; } = new NotRegisteredTextMeasurer();

    private sealed class NotRegisteredTextMeasurer : ITextMeasurer
    {
        private const string NotRegistered =
            "No text measurer is registered. Call SkiaTextMeasurer.Register().";

        public Size MeasureRuns(IReadOnlyList<TextRun> runs, Font baseFont) =>
            throw new InvalidOperationException(NotRegistered);

        public Size MeasureText(string text, Font font) =>
            throw new InvalidOperationException(NotRegistered);

        public float MeasureTextWidth(string text, int length, Font font) =>
            throw new InvalidOperationException(NotRegistered);

        // doesn't throw: this is asked before measuring, to decide whether
        // to wait for the font or draw with the fallback. "No" is safer than an exception
        public bool IsReady(Font font) => false;

        public Task PrepareAsync(Font font) => Task.CompletedTask;
    }
}