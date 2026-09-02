using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms.Controls.Text;

/// <summary>
/// Control with caption
/// </summary>
public class Label : UnitControl, ITextElement, IBorderedElement
{
    public string? Text { get; set; }
    public HorizontalContentAlignment HorizontalContentAlign { get; set; }
    public VerticalContentAlignment VerticalContentAlign { get; set; }
    public Color TextColor { get; set; } = Colors.Black;

    public Color BorderColor { get; set; } = Colors.Black;
    public float BorderWidth { get; set; } = 0f;

    /// <summary>Межстрочный интервал как множитель высоты строки.</summary>
    public float LineSpacing { get; set; } = 1.2f;

    private string[] SplitLines() =>
        (Text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    private float LineHeight =>
        TextMeasurer.Current.MeasureText("Wg", EffectiveFont).Height * LineSpacing;

    public override void Draw(Graphics g)
    {
        if (this.BorderWidth > 0)
            g.DrawRectangle(this.LocalBounds, this.BorderColor, this.BorderWidth);

        if (string.IsNullOrEmpty(Text))
            return;

        var content = this.ContentBounds;
        string[] lines = SplitLines();

        float lineHeight = LineHeight;
        float totalHeight = lineHeight * lines.Length;

        // блок строк выравнивается по вертикали целиком,
        // а каждая строка внутри своей полосы — по горизонтали
        float startY = VerticalContentAlign switch
        {
            VerticalContentAlignment.Top => content.Y,
            VerticalContentAlignment.Bottom => content.Y + content.Height - totalHeight,
            _ => content.Y + (content.Height - totalHeight) / 2f,
        };

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length == 0)
                continue;

            var lineRect = new Rectangle(
                new Point(content.X, startY + i * lineHeight),
                new Size(content.Width, lineHeight));

            g.DrawText(lines[i], lineRect, TextColor, EffectiveFont,
                HorizontalContentAlign, VerticalContentAlignment.Center);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (string.IsNullOrEmpty(Text))
            return ResolveSize(new Size(Padding.Horizontal, Padding.Vertical), availableSize);

        string[] lines = SplitLines();

        float maxWidth = 0;
        foreach (string line in lines)
            maxWidth = Math.Max(maxWidth, TextMeasurer.Current.MeasureText(line, EffectiveFont).Width);

        var content = new Size(
            maxWidth + Padding.Horizontal,
            LineHeight * lines.Length + Padding.Vertical);

        return ResolveSize(content, availableSize);
    }
}
