using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Раскладывает детей по сетке одинаковых ячеек.
/// Rows/Columns = 0 означает "посчитать автоматически".
/// </summary>
public class UniformGrid : PanelControl
{
    public int Rows { get; set; }
    public int Columns { get; set; }

    /// <summary>Сколько ячеек в первой строке пропустить (как FirstColumn в WPF).</summary>
    public int FirstColumn { get; set; }

    public float SpacingX { get; set; }
    public float SpacingY { get; set; }

    public override void Draw(Graphics g)
    {
        if (Background.A > 0)
            g.FillRectangle(this.LocalBounds, Background);
    }

    private (int Rows, int Columns) ResolveGrid()
    {
        int visible = 0;
        foreach (var child in Children)
            if (child.IsVisible) visible++;

        int cells = visible + Math.Max(0, FirstColumn);
        if (cells == 0) return (1, 1);

        int columns = Columns;
        int rows = Rows;

        if (columns > 0 && rows > 0)
            return (rows, columns);

        if (columns > 0)
            return ((int)Math.Ceiling(cells / (float)columns), columns);

        if (rows > 0)
            return (rows, (int)Math.Ceiling(cells / (float)rows));

        // обе оси авто — стремимся к квадрату, как это делает WPF
        columns = (int)Math.Ceiling(Math.Sqrt(cells));
        rows = (int)Math.Ceiling(cells / (float)columns);
        return (rows, columns);
    }

    private Size CellSize(Size area, int rows, int columns) => new(
        Math.Max(0, (area.Width - SpacingX * (columns - 1)) / columns),
        Math.Max(0, (area.Height - SpacingY * (rows - 1)) / rows));

    protected override Size MeasureContentOverride(Size availableSize)
    {
        var inner = new Size(
            Math.Max(0, availableSize.Width - Padding.Horizontal),
            Math.Max(0, availableSize.Height - Padding.Vertical));

        var (rows, columns) = ResolveGrid();
        Size cell = CellSize(inner, rows, columns);

        float maxChildWidth = 0;
        float maxChildHeight = 0;

        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;

            var m = child.Margin;
            child.Measure(new Size(
                Math.Max(0, cell.Width - m.Horizontal),
                Math.Max(0, cell.Height - m.Vertical)));

            maxChildWidth = Math.Max(maxChildWidth, child.DesiredSize.Width + m.Horizontal);
            maxChildHeight = Math.Max(maxChildHeight, child.DesiredSize.Height + m.Vertical);
        }

        // собственный желаемый размер — самая большая ячейка, размноженная на сетку
        var content = new Size(
            maxChildWidth * columns + SpacingX * (columns - 1) + Padding.Horizontal,
            maxChildHeight * rows + SpacingY * (rows - 1) + Padding.Vertical);

        return ResolveSize(content, availableSize);
    }

    protected override void ArrangeContentOverride(Size finalSize)
    {
        var area = new Rectangle(
            new Point(Padding.Left, Padding.Top),
            new Size(
                Math.Max(0, finalSize.Width - Padding.Horizontal),
                Math.Max(0, finalSize.Height - Padding.Vertical)));

        var (rows, columns) = ResolveGrid();
        Size cell = CellSize(area.Size, rows, columns);

        int index = Math.Max(0, FirstColumn);

        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;

            int row = index / columns;
            int column = index % columns;
            index++;

            var m = child.Margin;

            float x = area.X + column * (cell.Width + SpacingX) + m.Left;
            float y = area.Y + row * (cell.Height + SpacingY) + m.Top;

            child.Arrange(new Rectangle(
                new Point(x, y),
                new Size(
                    Math.Max(0, cell.Width - m.Horizontal),
                    Math.Max(0, cell.Height - m.Vertical))));
        }
    }
}
