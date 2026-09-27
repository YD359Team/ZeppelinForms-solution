using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Controls.Text;

/// <summary>A line of text assembled from runs with different formatting.</summary>
public class RichLabel : DecoratedControl
{
    public List<TextRun> Inlines { get; init; } = [];

    public HorizontalContentAlignment ContentAlign { get; set; } = HorizontalContentAlignment.Left;
    public VerticalContentAlignment ContentVerticalAlign { get; set; } = VerticalContentAlignment.Center;

    public void SetText(params TextRun[] runs)
    {
        Inlines.Clear();
        Inlines.AddRange(runs);
        Invalidate();
    }

    /// <summary>The runs with TextTransform applied. Without a transform the list
    /// itself is returned, so the usual case allocates nothing.</summary>
    private IReadOnlyList<TextRun> DisplayRuns
    {
        get
        {
            if (TextTransform == Enums.TextTransform.Normal) return Inlines;

            var runs = new TextRun[Inlines.Count];

            for (int i = 0; i < runs.Length; i++)
                runs[i] = Inlines[i] with { Text = ApplyTextTransform(Inlines[i].Text) };

            return runs;
        }
    }

    protected override void DrawContent(Graphics g)
    {
        if (Inlines.Count == 0) return;

        g.DrawRuns(DisplayRuns, ContentBounds, EffectiveFont, TextColor, ContentAlign, ContentVerticalAlign);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Inlines.Count == 0)
            return ResolveSize(new Size(Padding.Horizontal, Padding.Vertical), availableSize);

        Size text = TextMeasurer.Current.MeasureRuns(DisplayRuns, EffectiveFont);

        return ResolveSize(
            new Size(text.Width + Padding.Horizontal, text.Height + Padding.Vertical),
            availableSize);
    }
}