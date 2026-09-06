using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms.Controls.Text;

/// <summary>
/// Control with caption
/// </summary>
public class Label : DecoratedControl, ITextElement
{
    public string? Text { get; set; }

    public HorizontalContentAlignment HorizontalContentAlign { get; set; }
    public VerticalContentAlignment VerticalContentAlign { get; set; }

    public float LineSpacing { get; set; } = 1.2f;

    private string[] SplitLines() =>
        (Text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    private float LineHeight =>
        TextMeasurer.Current.MeasureText("Wg", EffectiveFont).Height * LineSpacing;

    // фон, рамка и скругление рисует база — здесь только текст
    protected override void DrawContent(Graphics g)
    {
        if (string.IsNullOrEmpty(Text)) return;

        var content = ContentBounds;
        string[] lines = SplitLines();

        float lineHeight = LineHeight;
        float totalHeight = lineHeight * lines.Length;

        float startY = VerticalContentAlign switch
        {
            VerticalContentAlignment.Top => content.Y,
            VerticalContentAlignment.Bottom => content.Y + content.Height - totalHeight,
            _ => content.Y + (content.Height - totalHeight) / 2f,
        };

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length == 0) continue;

            g.DrawText(lines[i],
                new Rectangle(new Point(content.X, startY + i * lineHeight),
                    new Size(content.Width, lineHeight)),
                TextColor, EffectiveFont,
                this.HorizontalContentAlign, VerticalContentAlignment.Center);
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

        return ResolveSize(
            new Size(maxWidth + Padding.Horizontal, LineHeight * lines.Length + Padding.Vertical),
            availableSize);
    }
}
