using System.Collections.Specialized;
using ZeppelinForms.Core.Collections;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Forms.Controls.Text;

/// <summary>A line of text assembled from runs with different formatting.</summary>
public partial class RichLabel : DecoratedControl
{
    /// <summary>The runs of the line. A run added or replaced measures the label again:
    /// since 0.20 a <see cref="ZfCollection{T}"/>, it was a list only <see cref="SetText"/>
    /// told the label about.</summary>
    public ZfCollection<TextRun> Inlines
    {
        get => _inlines;
        init => ZfCollectionOwner.Replace(ref _inlines, value, OnInlinesChanged);
    }

    private ZfCollection<TextRun> _inlines = [];

    public RichLabel()
    {
        _inlines.CollectionChanged += OnInlinesChanged;
    }

    private void OnInlinesChanged(object? sender, NotifyCollectionChangedEventArgs e) => Invalidate();

    /// <summary>Where the line stands across its room. Named and styled as on every
    /// other control with a caption.</summary>
    [Styled(Category = "Text")]
    public partial HorizontalContentAlignment HorizontalContentAlign { get; set; }
    private static HorizontalContentAlignment HorizontalContentAlignDefault => HorizontalContentAlignment.Left;

    /// <summary>Where the line stands up and down, when the label is taller than it.</summary>
    [Styled(Category = "Text")]
    public partial VerticalContentAlignment VerticalContentAlign { get; set; }
    private static VerticalContentAlignment VerticalContentAlignDefault => VerticalContentAlignment.Center;

    public void SetText(params TextRun[] runs)
    {
        // one change rather than a clear and a run each: the label is measured once
        Inlines.ReplaceAll(runs);
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

        g.DrawRuns(DisplayRuns, ContentBounds, EffectiveFont, TextColor, HorizontalContentAlign, VerticalContentAlign);
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