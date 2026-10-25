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
/// <remarks>
/// <para>
/// A line of the text is one row unless <see cref="TextWrapping"/> breaks it at the
/// edge of the room; a row that doesn't fit is cut off or trimmed with an ellipsis by
/// <see cref="TextTrimming"/>, and <see cref="MaxLines"/> caps the rows.
/// </para>
/// <para>
/// Each row is as tall as a line of the font times <see cref="LineSpacing"/> — the
/// font's measures, not the ink of the letters — so a label stands on the same
/// baseline as the caption of a button beside it.
/// </para>
/// </remarks>
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

    /// <summary>Whether the lines break into rows at the edge of the room. A label
    /// in a scrolling column, a note under a card, a description: Wrap. Off by
    /// default — a caption beside a field must stay one row.</summary>
    [Styled(Category = "Text", AffectsLayout = true)]
    public partial TextWrapping TextWrapping { get; set; }

    /// <summary>How a row that doesn't fit shows that it goes on: cut off, or
    /// shortened with "…". With <see cref="MaxLines"/>, the last row kept is trimmed.</summary>
    [Styled(Category = "Text", AffectsLayout = true)]
    public partial TextTrimming TextTrimming { get; set; }

    /// <summary>The most rows shown; 0 — as many as the text takes. The rows past it
    /// are dropped, and the last one kept is trimmed by <see cref="TextTrimming"/>.</summary>
    [Styled(Category = "Text", AffectsLayout = true)]
    public partial int MaxLines { get; set; }

    protected override void OnStyledPropertyChanged(StyledProperty property)
    {
        // the split into lines goes stale together with the text. It is reset
        // here rather than in a setter: a binding and ClearValue write the value
        // past the setter, and both come through this hook
        if (ReferenceEquals(property, TextProperty))
            _lines = null;

        // the rows depend on all of these; the cache key holds the rest
        _rows = null;

        base.OnStyledPropertyChanged(property);
    }

    /// <summary>Where the text stands across its room. A styled property: a style
    /// sheet sets it, and a change redraws at once.</summary>
    [Styled(Category = "Text")]
    public partial HorizontalContentAlignment HorizontalContentAlign { get; set; }
    private static HorizontalContentAlignment HorizontalContentAlignDefault => HorizontalContentAlignment.Left;

    /// <summary>Where the text stands up and down in its room, when the control is
    /// taller than its text.</summary>
    /// <remarks>Center by default, as every other control's caption: until 0.15 it was
    /// Top, and the text of a label stretched to the height of a status bar or of a
    /// row stood at its top edge.</remarks>
    [Styled(Category = "Text")]
    public partial VerticalContentAlignment VerticalContentAlign { get; set; }
    private static VerticalContentAlignment VerticalContentAlignDefault => VerticalContentAlignment.Center;

    /// <summary>The height of a row as a multiple of the font's line. 1 by default:
    /// rows as tall as the font asks for, as a button's caption is.</summary>
    /// <remarks>Until 0.15 it multiplied the ink height of "Wg", and its default of
    /// 1.2 made up for the ink being shorter than the font's line.</remarks>
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
    } = 1f;

    private string[] SplitLines() =>
        (ShownText ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    /// <summary>The text as shown: the text, or — never set — the name given to the
    /// label, see <see cref="ZeppelinForms.Forms.Controls.Base.UIElement.NameCaption"/>.</summary>
    internal override string? ShownText => Text ?? NameCaption;

    protected override void OnNameCaptionChanged()
    {
        if (Text is not null) return;

        // the lines are of the name now
        _lines = null;
        _rows = null;
        Invalidate();
    }

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
        TextMeasurer.LineHeight(EffectiveFont) * LineSpacing;

    // ===== rows =====

    /// <summary>A row on screen: the text shown, with the spaces of a soft break left
    /// out, and the access key's place in it — −1 when it is not in this row.</summary>
    private readonly record struct LabelRow(string Text, int AccessKey);

    private IReadOnlyList<LabelRow>? _rows;
    private (float Width, Font Font, TextTransform Transform, bool Mnemonic) _rowsKey;

    /// <summary>The widest of <see cref="_rows"/>: what the label measured to.</summary>
    private float _rowsWidth;

    /// <summary>The rows at a width of the content. Kept until the width, the font or
    /// the case changes — measuring and drawing both ask.</summary>
    /// <remarks>
    /// Rows laid out for more room are kept for less as long as every one of them
    /// fits it. A label not stretched is arranged at the width its rows took, and
    /// laying them out again there shortened what had already been shortened: "one
    /// two…" measured in 100 px became "one…" in the 67 px it was given.
    /// </remarks>
    private IReadOnlyList<LabelRow> Rows(float width)
    {
        Font font = EffectiveFont;
        var key = (width, font, TextTransform, UseMnemonic);

        if (_rows is not null)
        {
            if (_rowsKey.Equals(key)) return _rows;

            bool sameText = _rowsKey.Font == font
                && _rowsKey.Transform == TextTransform
                && _rowsKey.Mnemonic == UseMnemonic;

            if (sameText && _rowsKey.Width > width && _rowsWidth <= width + 0.01f) return _rows;
        }

        var rows = new List<LabelRow>();
        bool wrap = TextWrapping == TextWrapping.Wrap && width > 0 && float.IsFinite(width);

        foreach (string line in Lines)
        {
            // the access key is read per line: its mark sits in the line as written
            (string shown, int accessKey) = UseMnemonic ? Mnemonic.Parse(line) : (line, -1);
            shown = ApplyTextTransformCase(shown);

            if (!wrap)
            {
                rows.Add(new LabelRow(shown, accessKey));
                continue;
            }

            foreach (var (start, length) in TextWrapper.Wrap(shown, width, font))
            {
                int key2 = accessKey >= start && accessKey < start + length ? accessKey - start : -1;

                // the spaces a row breaks after don't count when it is aligned
                rows.Add(new LabelRow(shown.Substring(start, length).TrimEnd(' ', '\t'), key2));
            }
        }

        Trim(rows, width, font);

        _rows = rows;
        _rowsKey = key;
        _rowsWidth = 0;

        foreach (LabelRow row in rows)
            _rowsWidth = Math.Max(_rowsWidth, TextMeasurer.Current.MeasureTextWidth(row.Text, row.Text.Length, font));

        return rows;
    }

    /// <summary>Drop the rows past <see cref="MaxLines"/> and shorten what doesn't fit,
    /// as <see cref="TextTrimming"/> says.</summary>
    private void Trim(List<LabelRow> rows, float width, Font font)
    {
        bool cut = MaxLines > 0 && rows.Count > MaxLines;

        if (cut) rows.RemoveRange(MaxLines, rows.Count - MaxLines);

        if (TextTrimming == TextTrimming.None || !(width > 0) || !float.IsFinite(width)) return;

        bool atWord = TextTrimming == TextTrimming.WordEllipsis;

        for (int i = 0; i < rows.Count; i++)
        {
            LabelRow row = rows[i];

            // the last row kept goes on in the dropped ones: it ends with "…" even
            // if it fits
            bool continues = cut && i == rows.Count - 1;
            string text = continues
                ? TextWrapper.Ellipsize(row.Text + TextWrapper.Ellipsis, width, font, atWord)
                : TextWrapper.Ellipsize(row.Text, width, font, atWord);

            if (!ReferenceEquals(text, row.Text))
                rows[i] = new LabelRow(text, row.AccessKey < text.Length - 1 ? row.AccessKey : -1);
        }
    }

    /// <summary>The case transform alone: the marks are already read out of the line.</summary>
    private string ApplyTextTransformCase(string text) => TransformCase(text);

    // the background, border and corner radius are drawn by the base — only the text here
    protected override void DrawContent(Graphics g)
    {
        if (string.IsNullOrEmpty(ShownText)) return;

        var content = ContentBounds;
        IReadOnlyList<LabelRow> rows = Rows(content.Width);

        float lineHeight = LineHeight;
        float totalHeight = lineHeight * rows.Count;

        float startY = VerticalContentAlign switch
        {
            VerticalContentAlignment.Top => content.Y,
            VerticalContentAlignment.Bottom => content.Y + content.Height - totalHeight,
            _ => content.Y + (content.Height - totalHeight) / 2f,
        };

        bool showsAccessKeys = UseMnemonic && FindOwner() is { ShowsAccessKeys: true };

        for (int i = 0; i < rows.Count; i++)
        {
            LabelRow row = rows[i];

            if (row.Text.Length == 0) continue;

            var lineRect = new Rectangle(new Point(content.X, startY + i * lineHeight),
                new Size(content.Width, lineHeight));

            g.DrawText(row.Text, lineRect,
                TextColor, EffectiveFont,
                this.HorizontalContentAlign, VerticalContentAlignment.Center);

            // per line: the mark is in whichever line it fell into after wrapping
            if (showsAccessKeys && row.AccessKey >= 0)
                Mnemonic.DrawUnderline(g, row.Text, row.AccessKey, lineRect, TextColor, EffectiveFont,
                    this.HorizontalContentAlign, VerticalContentAlignment.Center);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (string.IsNullOrEmpty(Text))
            return ResolveSize(new Size(Padding.Horizontal, Padding.Vertical), availableSize);

        // the room for the text: the given width, or this label's own when it has one
        float room = !float.IsNaN(Size.Width) ? Size.Width : availableSize.Width;
        IReadOnlyList<LabelRow> rows = Rows(Math.Max(0, room - Padding.Horizontal));

        return ResolveSize(
            new Size(_rowsWidth + Padding.Horizontal, LineHeight * rows.Count + Padding.Vertical),
            availableSize);
    }
}
