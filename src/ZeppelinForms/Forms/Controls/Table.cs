using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// A read-only table: rows are text, columns work as in HTML or Markdown.
/// Scrolls by itself, the header stays in place.
/// Cells cannot contain controls — Grid is for that.
/// </summary>
public partial class Table : DecoratedPanel
{
    private readonly List<float> _widths = [];

    private float _headerHeight;

    /// <summary>The row height actually used: RowHeight, or computed from the font.</summary>
    /// <remarks>
    /// Not named _rowHeight: that is the field the generator creates for the
    /// RowHeight property itself. Measuring used to write the computed height
    /// there, and after the first layout RowHeight stayed non-zero for good —
    /// a font change no longer recomputed the rows, and PropertyGrid showed
    /// a value nobody had set.
    /// </remarks>
    private float _computedRowHeight;

    public List<TableColumn> Columns { get; init; } = [];

    /// <summary>Rows. Extra cells are ignored, missing ones are drawn empty.</summary>
    public List<string?[]> Rows { get; init; } = [];

    [Styled(Category = "Table", AffectsLayout = true)]
    public partial bool ShowHeader { get; set; }

    private static bool ShowHeaderDefault => true;

    [Styled(Category = "Table")]
    public partial bool ShowGridLines { get; set; }

    private static bool ShowGridLinesDefault => true;

    [Styled(Category = "Table", AffectsLayout = true)]
    public partial Thickness CellPadding { get; set; }

    private static Thickness CellPaddingDefault => new(8, 4);

    /// <summary>Row height. Zero — compute from the font.</summary>
    [Styled(Category = "Table", AffectsLayout = true)]
    public partial float RowHeight { get; set; }

    [Styled(Category = "Table")]
    public partial Color GridLineColor { get; set; }

    [Styled(Category = "Table")]
    public partial Color HeaderColor { get; set; }

    private static Color HeaderColorDefault => new(255, 244, 244, 244);

    [Styled(Category = "Table")]
    public partial Color HeaderTextColor { get; set; }
    private static Color HeaderTextColorDefault => Colors.Black;

    private static Color GridLineColorDefault => new(255, 224, 224, 224);

    /// <summary>Highlight every other row.</summary>
    [Styled(Category = "Table")]
    public partial bool ShowAlternateRows { get; set; }

    private static bool ShowAlternateRowsDefault => true;

    /// <summary>The stripe color of even rows. Transparent — derive it
    /// from the text color, so that it works in any theme.</summary>
    [Styled(Category = "Table")]
    public partial Color AlternateRowColor { get; set; }

    /// <summary>Add a row. Rows is a plain list and says nothing about changes,
    /// so a row added through it directly needs an explicit Invalidate.</summary>
    public void AddRow(params string?[] cells)
    {
        Rows.Add(cells);

        // a new row changes the height and possibly the Auto column widths
        Invalidate();
    }

    /// <summary>A translucent shade of the text color. Stripes and lines built
    /// this way read equally well on a light and a dark theme: on a light one
    /// it's a slight darkening, on a dark one a lightening.</summary>
    private Color Tint(byte alpha) => new(alpha, TextColor.R, TextColor.G, TextColor.B);

    /// <summary>The header font: like the content's, but bold.</summary>
    private Font HeaderFont => EffectiveFont.Bold();

    public Table()
    {
        // a table sizes to its content, as in Markdown. It can be stretched
        // manually, and then the star columns share the extra width
        SetControlDefault(HorizontalAlignmentProperty, HorizontalAlignment.Left);
        SetControlDefault(VerticalAlignmentProperty, VerticalAlignment.Top);
    }

    // ===== layout =====

    protected override Size MeasureContentOverride(Size availableSize)
    {
        float lineHeight = TextMeasurer.Current.MeasureText("Wg", EffectiveFont).Height;

        _computedRowHeight = RowHeight > 0 ? RowHeight : lineHeight + CellPadding.Vertical;
        _headerHeight = ShowHeader ? _computedRowHeight : 0;

        float inner = Math.Max(0, availableSize.Width - Padding.Horizontal);

        ResolveWidths(inner);

        float total = 0;
        foreach (float width in _widths) total += width;

        return new Size(
            total + Padding.Horizontal,
            _headerHeight + _computedRowHeight * Rows.Count + Padding.Vertical);
    }

    /// <summary>Column widths: first Auto and fixed ones,
    /// the star columns share the remainder by weight.</summary>
    private void ResolveWidths(float available)
    {
        _widths.Clear();

        float taken = 0;
        float starWeight = 0;

        foreach (TableColumn column in Columns)
        {
            if (column.Width.IsStar)
            {
                starWeight += column.Width.Value;
                _widths.Add(0);
                continue;
            }

            float width = column.Width.IsAuto
                ? ContentWidth(Columns.IndexOf(column))
                : column.Width.Value;

            _widths.Add(width);
            taken += width;
        }

        if (starWeight <= 0) return;

        // the width is unknown — the table is inside an infinite axis. The star
        // columns have nothing to share, so they behave like Auto
        float rest = float.IsFinite(available) ? Math.Max(0, available - taken) : 0;

        for (int i = 0; i < Columns.Count; i++)
        {
            if (!Columns[i].Width.IsStar) continue;

            _widths[i] = rest > 0
                ? rest * Columns[i].Width.Value / starWeight
                : ContentWidth(i);
        }
    }

    /// <summary>The column width by the longest value together with the header.</summary>
    private float ContentWidth(int column)
    {
        float widest = ShowHeader && Columns[column].Header is { } header
            ? TextMeasurer.Current.MeasureText(header, HeaderFont).Width
            : 0;

        foreach (string?[] row in Rows)
        {
            if (column >= row.Length || row[column] is not { } cell) continue;

            widest = Math.Max(widest, TextMeasurer.Current.MeasureText(cell, EffectiveFont).Width);
        }

        return widest + CellPadding.Horizontal;
    }

    // no children: nothing to arrange, everything is drawn directly
    protected override void ArrangeContentOverride(Size contentSize) { }

    // ===== drawing =====

    protected override void DrawContent(Graphics g)
    {
        if (Columns.Count == 0 || _widths.Count != Columns.Count) return;

        Rectangle view = Viewport;

        // rows live below the header and must not slide under it
        var band = new Rectangle(
            new Point(view.X, view.Y + _headerHeight),
            new Size(view.Width, Math.Max(0, view.Height - _headerHeight)));

        if (band.Height <= 0 || band.Width <= 0) return;

        g.Save();
        g.ClipRect(band);

        // only visible rows are drawn: on ten thousand rows
        // a pass over all of them would eat the frame
        int first = Math.Max(0, (int)(ScrollY / _computedRowHeight));
        int last = Math.Min(Rows.Count, first + (int)(band.Height / _computedRowHeight) + 2);

        for (int i = first; i < last; i++)
            DrawRow(g, i, band.Y + i * _computedRowHeight - ScrollY, view.Width);

        if (ShowGridLines)
            DrawVerticalLines(g, band);

        g.Restore();
    }

    private void DrawRow(Graphics g, int index, float top, float width)
    {
        var bounds = new Rectangle(
            new Point(Viewport.X - ScrollX, top),
            new Size(width + ScrollX, _computedRowHeight));

        if (ShowAlternateRows && index % 2 == 1)
            g.FillRectangle(bounds, AlternateRowColor.A > 0 ? AlternateRowColor : Tint(12));

        Color line = GridLineColor.A > 0 ? GridLineColor : Tint(30);
        if (ShowGridLines)
            g.DrawLine(
                new Point(bounds.X, top + _computedRowHeight),
                new Point(bounds.X + bounds.Width, top + _computedRowHeight),
                line, 1f);

        string?[] row = Rows[index];
        float x = Viewport.X - ScrollX;

        for (int c = 0; c < Columns.Count; c++)
        {
            string? cell = c < row.Length ? row[c] : null;

            if (!string.IsNullOrEmpty(cell))
                DrawCell(g, cell, x, top, _widths[c], Columns[c].Align, TextColor);

            x += _widths[c];
        }
    }

    private void DrawCell(
        Graphics g, string text, float x, float top, float width,
        HorizontalContentAlignment align, Color color, Font? font = null)
    {
        var cell = new Rectangle(
            new Point(x + CellPadding.Left, top),
            new Size(Math.Max(0, width - CellPadding.Horizontal), _computedRowHeight));

        g.DrawText(text, cell, color, font ?? EffectiveFont, align, VerticalContentAlignment.Center);
    }

    private void DrawVerticalLines(Graphics g, Rectangle band)
    {
        float x = Viewport.X - ScrollX;
        Color line = GridLineColor.A > 0 ? GridLineColor : Tint(30);

        // the last boundary is not drawn: it would coincide with the table's border
        for (int c = 0; c < Columns.Count - 1; c++)
        {
            x += _widths[c];

            g.DrawLine(
                new Point(x, band.Y),
                new Point(x, band.Y + band.Height),
                line, 1f);
        }
    }

    /// <summary>The header. DrawDecoration is called after the children and
    /// outside their clip, so it stays in place while scrolling.</summary>
    /// <remarks>
    /// The header used to be drawn twice: the background was filled twice, and
    /// a second pass over the captions continued x from the right edge, so it
    /// drew in the regular font beyond the border, where the clip threw it away.
    /// A thin GridLineColor line was also drawn over the thick rule and repainted
    /// its middle. All three were duplicates and are gone.
    /// </remarks>
    protected override void DrawDecoration(Graphics g)
    {
        if (!ShowHeader || Columns.Count == 0 || _widths.Count != Columns.Count) return;

        Rectangle view = Viewport;

        var bounds = new Rectangle(view.Position, new Size(view.Width, _headerHeight));

        g.Save();
        g.ClipRect(bounds);

        if (HeaderColor.A > 0)
            g.FillRectangle(bounds, HeaderColor);

        float x = view.X - ScrollX;

        for (int c = 0; c < Columns.Count; c++)
        {
            if (Columns[c].Header is { } header && header.Length > 0)
                DrawCell(g, header, x, view.Y, _widths[c], Columns[c].Align, HeaderTextColor, HeaderFont);

            x += _widths[c];
        }

        // the rule under the header is noticeably thicker than the grid — that is
        // its role in a Markdown table, to separate the head from the data
        g.DrawLine(
            new Point(bounds.X, bounds.Y + _headerHeight),
            new Point(bounds.X + bounds.Width, bounds.Y + _headerHeight),
            Tint(110), 2f);

        g.Restore();
    }
}