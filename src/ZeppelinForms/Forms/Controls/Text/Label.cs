using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Forms.Controls.Text;

/// <summary>
/// Control with caption
/// </summary>
public partial class Label : DecoratedControl, ITextElement
{
    private string[]? _lines;

    /// <summary>The caption text. A styled property, so it can be a binding
    /// target: <c>label.Bind(Label.TextProperty, source, nameof(Source.Name))</c>.</summary>
    /// <remarks>
    /// The label's size is computed from its text, so a change needs not just
    /// a redraw but a layout pass — hence AffectsLayout.
    /// </remarks>
    [Styled(Category = "Text", AffectsLayout = true)]
    public partial string? Text { get; set; }

    protected override void OnStyledPropertyChanged(StyledProperty property)
    {
        // the split into lines goes stale together with the text. It is reset
        // here rather than in a setter: a binding and ClearValue write the value
        // past the setter, and both come through this hook
        if (ReferenceEquals(property, TextProperty))
            _lines = null;

        base.OnStyledPropertyChanged(property);
    }

    public HorizontalContentAlignment HorizontalContentAlign { get; set; }
    public VerticalContentAlignment VerticalContentAlign { get; set; }

    public float LineSpacing
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // line spacing is part of a multi-line label's height
            Invalidate();
        }
    } = 1.2f;

    private string[] SplitLines() =>
        (Text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    /// <summary>The split into lines. Computed once per text: both drawing
    /// and measuring ask for it many times per frame, and Split creates
    /// a new array every time.</summary>
    /// <remarks>
    /// The lines are cached as written; TextTransform is applied when drawing
    /// and measuring. It is inherited, and an ancestor changing it never reaches
    /// this label's OnStyledPropertyChanged, so a cache of transformed lines
    /// would go stale unnoticed.
    /// </remarks>
    private string[] Lines => _lines ??= SplitLines();

    private float LineHeight =>
        TextMeasurer.Current.MeasureText("Wg", EffectiveFont).Height * LineSpacing;

    // the background, border and corner radius are drawn by the base — only the text here
    protected override void DrawContent(Graphics g)
    {
        if (string.IsNullOrEmpty(Text)) return;

        var content = ContentBounds;
        string[] lines = Lines;

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

            g.DrawText(ApplyTextTransform(lines[i]),
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

        string[] lines = Lines;

        float maxWidth = 0;
        foreach (string line in lines)
            maxWidth = Math.Max(maxWidth,
                TextMeasurer.Current.MeasureText(ApplyTextTransform(line), EffectiveFont).Width);

        return ResolveSize(
            new Size(maxWidth + Padding.Horizontal, LineHeight * lines.Length + Padding.Vertical),
            availableSize);
    }
}