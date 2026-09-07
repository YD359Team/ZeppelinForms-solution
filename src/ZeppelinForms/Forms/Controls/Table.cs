using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Таблица только для чтения: строки — текст, столбцы — как в HTML или
/// Markdown. Прокручивается сама, заголовок остаётся на месте.
/// Ячейки не могут содержать контролов — для этого есть Grid.
/// </summary>
public partial class Table : DecoratedPanel
{
    private readonly List<float> _widths = [];

    private float _headerHeight;

    public List<TableColumn> Columns { get; init; } = [];

    /// <summary>Строки. Лишние ячейки игнорируются, недостающие рисуются пустыми.</summary>
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

    /// <summary>Высота строки. Ноль — считать по шрифту.</summary>
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

    /// <summary>Подсвечивать каждую вторую строку.</summary>
    [Styled(Category = "Table")]
    public partial bool ShowAlternateRows { get; set; }

    private static bool ShowAlternateRowsDefault => true;

    /// <summary>Цвет подложки чётных строк. Прозрачный — вывести
    /// из цвета текста, чтобы работало в любой теме.</summary>
    [Styled(Category = "Table")]
    public partial Color AlternateRowColor { get; set; }

    public void AddRow(params string?[] cells) => Rows.Add(cells);

    /// <summary>Полупрозрачный оттенок цвета текста. Подложки и линии,
    /// построенные так, одинаково читаются и на светлой, и на тёмной теме:
    /// на светлой это лёгкое затемнение, на тёмной — осветление.</summary>
    private Color Tint(byte alpha) => new(alpha, TextColor.R, TextColor.G, TextColor.B);

    /// <summary>Шрифт заголовка: как у содержимого, но жирный.</summary>
    private Font HeaderFont => EffectiveFont.Bold();

    public Table()
    {
        // таблица — по содержимому, как в Markdown. Растянуть можно вручную,
        // и тогда столбцы со звёздочкой поделят лишнюю ширину
        SetControlDefault(HorizontalAlignmentProperty, HorizontalAlignment.Left);
        SetControlDefault(VerticalAlignmentProperty, VerticalAlignment.Top);
    }

    // ===== раскладка =====

    protected override Size MeasureContentOverride(Size availableSize)
    {
        float lineHeight = TextMeasurer.Current.MeasureText("Wg", EffectiveFont).Height;

        _rowHeight = RowHeight > 0 ? RowHeight : lineHeight + CellPadding.Vertical;
        _headerHeight = ShowHeader ? _rowHeight : 0;

        float inner = Math.Max(0, availableSize.Width - Padding.Horizontal);

        ResolveWidths(inner);

        float total = 0;
        foreach (float width in _widths) total += width;

        return new Size(
            total + Padding.Horizontal,
            _headerHeight + _rowHeight * Rows.Count + Padding.Vertical);
    }

    /// <summary>Ширины столбцов: сначала Auto и фиксированные,
    /// остаток делят звёздочки по весу.</summary>
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

        // ширина неизвестна — таблица внутри бесконечной оси. Звёздочкам
        // делить нечего, поэтому ведут себя как Auto
        float rest = float.IsFinite(available) ? Math.Max(0, available - taken) : 0;

        for (int i = 0; i < Columns.Count; i++)
        {
            if (!Columns[i].Width.IsStar) continue;

            _widths[i] = rest > 0
                ? rest * Columns[i].Width.Value / starWeight
                : ContentWidth(i);
        }
    }

    /// <summary>Ширина столбца по самому длинному значению вместе с заголовком.</summary>
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

    // потомков нет: раскладывать нечего, всё рисуется напрямую
    protected override void ArrangeContentOverride(Size contentSize) { }

    // ===== отрисовка =====

    protected override void DrawContent(Graphics g)
    {
        if (Columns.Count == 0 || _widths.Count != Columns.Count) return;

        Rectangle view = Viewport;

        // строки живут ниже заголовка и не должны под него заезжать
        var band = new Rectangle(
            new Point(view.X, view.Y + _headerHeight),
            new Size(view.Width, Math.Max(0, view.Height - _headerHeight)));

        if (band.Height <= 0 || band.Width <= 0) return;

        g.Save();
        g.ClipRect(band);

        // рисуем только видимые строки: на десяти тысячах строк
        // проход по всем съел бы кадр
        int first = Math.Max(0, (int)(ScrollY / _rowHeight));
        int last = Math.Min(Rows.Count, first + (int)(band.Height / _rowHeight) + 2);

        for (int i = first; i < last; i++)
            DrawRow(g, i, band.Y + i * _rowHeight - ScrollY, view.Width);

        if (ShowGridLines)
            DrawVerticalLines(g, band);

        g.Restore();
    }

    private void DrawRow(Graphics g, int index, float top, float width)
    {
        var bounds = new Rectangle(
            new Point(Viewport.X - ScrollX, top),
            new Size(width + ScrollX, _rowHeight));

        if (ShowAlternateRows && index % 2 == 1)
            g.FillRectangle(bounds, AlternateRowColor.A > 0 ? AlternateRowColor : Tint(12));

        Color line = GridLineColor.A > 0 ? GridLineColor : Tint(30);
        if (ShowGridLines)
            g.DrawLine(
                new Point(bounds.X, top + _rowHeight),
                new Point(bounds.X + bounds.Width, top + _rowHeight),
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
            new Size(Math.Max(0, width - CellPadding.Horizontal), _rowHeight));

        g.DrawText(text, cell, color, font ?? EffectiveFont, align, VerticalContentAlignment.Center);
    }

    private void DrawVerticalLines(Graphics g, Rectangle band)
    {
        float x = Viewport.X - ScrollX;
        Color line = GridLineColor.A > 0 ? GridLineColor : Tint(30);

        // последнюю границу не рисуем: она совпала бы с рамкой таблицы
        for (int c = 0; c < Columns.Count - 1; c++)
        {
            x += _widths[c];

            g.DrawLine(
                new Point(x, band.Y),
                new Point(x, band.Y + band.Height),
                line, 1f);
        }
    }

    /// <summary>Заголовок. DrawDecoration вызывается после потомков и вне
    /// их отсечения, поэтому он остаётся на месте при прокрутке.</summary>
    protected override void DrawDecoration(Graphics g)
    {
        if (!ShowHeader || Columns.Count == 0 || _widths.Count != Columns.Count) return;

        Rectangle view = Viewport;

        var bounds = new Rectangle(view.Position, new Size(view.Width, _headerHeight));

        g.Save();
        g.ClipRect(bounds);

        g.FillRectangle(bounds, HeaderColor);

        if (HeaderColor.A > 0)
            g.FillRectangle(bounds, HeaderColor);

        float x = view.X - ScrollX;

        for (int c = 0; c < Columns.Count; c++)
        {
            if (Columns[c].Header is { } header && header.Length > 0)
                DrawCell(g, header, x, view.Y, _widths[c], Columns[c].Align, HeaderTextColor, HeaderFont);

            x += _widths[c];
        }

        // линейка под заголовком заметно толще сетки — это её роль
        // в Markdown-таблице, отделять шапку от данных
        g.DrawLine(
            new Point(bounds.X, bounds.Y + _headerHeight),
            new Point(bounds.X + bounds.Width, bounds.Y + _headerHeight),
            Tint(110), 2f);

        for (int c = 0; c < Columns.Count; c++)
        {
            if (Columns[c].Header is { } header && header.Length > 0)
                DrawCell(g, header, x, view.Y, _widths[c], Columns[c].Align, HeaderTextColor);

            x += _widths[c];
        }

        if (ShowGridLines)
            g.DrawLine(
                new Point(bounds.X, bounds.Y + _headerHeight),
                new Point(bounds.X + bounds.Width, bounds.Y + _headerHeight),
                GridLineColor, 1f);

        g.Restore();
    }
}