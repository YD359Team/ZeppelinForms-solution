using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Controls.Text;

/// <summary>Строка текста, собранная из прогонов с разным оформлением.</summary>
public class RichLabel : DecoratedControl
{
    public List<TextRun> Inlines { get; init; } = [];

    public Color TextColor { get; set; } = Colors.Black;

    public HorizontalContentAlignment ContentAlign { get; set; } = HorizontalContentAlignment.Left;
    public VerticalContentAlignment ContentVerticalAlign { get; set; } = VerticalContentAlignment.Center;

    public void SetText(params TextRun[] runs)
    {
        Inlines.Clear();
        Inlines.AddRange(runs);
        Invalidate();
    }

    protected override void DrawContent(Graphics g)
    {
        if (Inlines.Count == 0) return;

        g.DrawRuns(Inlines, ContentBounds, EffectiveFont, TextColor, ContentAlign, ContentVerticalAlign);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Inlines.Count == 0)
            return ResolveSize(new Size(Padding.Horizontal, Padding.Vertical), availableSize);

        Size text = TextMeasurer.Current.MeasureRuns(Inlines, EffectiveFont);

        return ResolveSize(
            new Size(text.Width + Padding.Horizontal, text.Height + Padding.Vertical),
            availableSize);
    }
}