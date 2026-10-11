using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Drawing;

public static class TextMeasurer
{
    public static ITextMeasurer Current { get; set; } = new NotRegisteredTextMeasurer();

    /// <summary>The height of one line of text in the font, whatever the letters:
    /// what a caption of one line is sized by.</summary>
    public static float LineHeight(Font font) => Current.MeasureFont(font).LineHeight;

    /// <summary>The box of a line of text: as wide as its letters advance, as tall
    /// as a line of the font — the same for "OK" as for "Apply", and for an empty
    /// field as for a filled one.</summary>
    public static Size MeasureLine(string? text, Font font) =>
        new(string.IsNullOrEmpty(text) ? 0f : Current.MeasureText(text, font).Width, LineHeight(font));

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

        public FontMetrics MeasureFont(Font font) =>
            throw new InvalidOperationException(NotRegistered);

        // doesn't throw: this is asked before measuring, to decide whether
        // to wait for the font or draw with the fallback. "No" is safer than an exception
        public bool IsReady(Font font) => false;

        public Task PrepareAsync(Font font) => Task.CompletedTask;
    }
}