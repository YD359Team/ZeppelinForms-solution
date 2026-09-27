using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Lays out children in a grid of equal cells.
/// Rows/Columns = 0 means "compute automatically".
/// </summary>
public class UniformGrid : DecoratedPanel
{
    // every one of these changes the cells, so each asks for a layout pass:
    // previously a grid that was already shown kept its old cells
    public int Rows
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Invalidate();
        }
    }

    public int Columns
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Invalidate();
        }
    }

    public int FirstColumn
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Invalidate();
        }
    }

    public float SpacingX
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Invalidate();
        }
    }

    public float SpacingY
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Invalidate();
        }
    }

    /// <summary>Placement into cells, taking into account that elements
    /// may occupy several cells at once.</summary>
    private (int Rows, int Columns, List<(UIElement Child, int Row, int Column)> Placement) BuildPlacement()
    {
        List<UIElement> visible = [];

        foreach (UIElement child in Children)
            if (child.IsVisible)
                visible.Add(child);

        int columns = Columns;
        int rows = Rows;

        if (columns <= 0)
        {
            // count the total area in cells rather than the number of elements —
            // otherwise wide elements won't fit into the chosen grid
            int cells = Math.Max(0, FirstColumn);

            foreach (UIElement child in visible)
                cells += Math.Max(1, child.ColumnSpan) * Math.Max(1, child.RowSpan);

            if (cells == 0) return (1, 1, []);

            columns = rows > 0
                ? (int)Math.Ceiling(cells / (float)rows)
                : (int)Math.Ceiling(Math.Sqrt(cells));
        }

        columns = Math.Max(1, columns);

        // cell occupancy: an element with span > 1 blocks several
        List<bool[]> occupied = [];
        List<(UIElement, int, int)> placement = [];

        bool IsFree(int row, int column, int rowSpan, int columnSpan)
        {
            if (column + columnSpan > columns) return false;

            for (int r = row; r < row + rowSpan; r++)
            {
                while (occupied.Count <= r)
                    occupied.Add(new bool[columns]);

                for (int c = column; c < column + columnSpan; c++)
                    if (occupied[r][c]) return false;
            }

            return true;
        }

        void Occupy(int row, int column, int rowSpan, int columnSpan)
        {
            for (int r = row; r < row + rowSpan; r++)
            {
                while (occupied.Count <= r)
                    occupied.Add(new bool[columns]);

                for (int c = column; c < column + columnSpan; c++)
                    occupied[r][c] = true;
            }
        }

        Occupy(0, 0, 1, Math.Min(Math.Max(0, FirstColumn), columns));

        foreach (UIElement child in visible)
        {
            int columnSpan = Math.Clamp(child.ColumnSpan, 1, columns);
            int rowSpan = Math.Max(1, child.RowSpan);

            int row = 0;
            bool placed = false;

            while (!placed)
            {
                for (int column = 0; column < columns; column++)
                {
                    if (!IsFree(row, column, rowSpan, columnSpan)) continue;

                    Occupy(row, column, rowSpan, columnSpan);
                    placement.Add((child, row, column));

                    placed = true;
                    break;
                }

                if (!placed) row++;
            }
        }

        int usedRows = Math.Max(rows, occupied.Count);

        return (Math.Max(1, usedRows), columns, placement);
    }

    private Size CellSize(Size area, int rows, int columns) => new(
        Math.Max(0, (area.Width - SpacingX * (columns - 1)) / columns),
        Math.Max(0, (area.Height - SpacingY * (rows - 1)) / rows));

    protected override Size MeasureContentOverride(Size availableSize)
    {
        var inner = new Size(
            Math.Max(0, availableSize.Width - Padding.Horizontal),
            Math.Max(0, availableSize.Height - Padding.Vertical));

        var (rows, columns, placement) = BuildPlacement();
        Size cell = CellSize(inner, rows, columns);

        float maxCellWidth = 0;
        float maxCellHeight = 0;

        foreach ((UIElement child, _, _) in placement)
        {
            Thickness m = child.Margin;

            int columnSpan = Math.Clamp(child.ColumnSpan, 1, columns);
            int rowSpan = Math.Max(1, child.RowSpan);

            var childAvailable = new Size(
                Math.Max(0, cell.Width * columnSpan + SpacingX * (columnSpan - 1) - m.Horizontal),
                Math.Max(0, cell.Height * rowSpan + SpacingY * (rowSpan - 1) - m.Vertical));

            child.Measure(childAvailable);

            // bring the desired size to the size of one cell,
            // otherwise a wide element would inflate the whole grid
            maxCellWidth = Math.Max(maxCellWidth, (child.DesiredSize.Width + m.Horizontal) / columnSpan);
            maxCellHeight = Math.Max(maxCellHeight, (child.DesiredSize.Height + m.Vertical) / rowSpan);
        }

        var content = new Size(
            maxCellWidth * columns + SpacingX * (columns - 1) + Padding.Horizontal,
            maxCellHeight * rows + SpacingY * (rows - 1) + Padding.Vertical);

        return ResolveSize(content, availableSize);
    }

    protected override void ArrangeContentOverride(Size contentSize)
    {
        var area = new Rectangle(
            new Point(Padding.Left, Padding.Top),
            new Size(
                Math.Max(0, contentSize.Width - Padding.Horizontal),
                Math.Max(0, contentSize.Height - Padding.Vertical)));

        var (rows, columns, placement) = BuildPlacement();
        Size cell = CellSize(area.Size, rows, columns);

        foreach ((UIElement child, int row, int column) in placement)
        {
            Thickness m = child.Margin;

            int columnSpan = Math.Clamp(child.ColumnSpan, 1, columns);
            int rowSpan = Math.Max(1, child.RowSpan);

            float x = area.X + column * (cell.Width + SpacingX) + m.Left;
            float y = area.Y + row * (cell.Height + SpacingY) + m.Top;

            float width = cell.Width * columnSpan + SpacingX * (columnSpan - 1) - m.Horizontal;
            float height = cell.Height * rowSpan + SpacingY * (rowSpan - 1) - m.Vertical;

            child.Arrange(new Rectangle(
                new Point(x, y),
                new Size(Math.Max(0, width), Math.Max(0, height))));
        }
    }
}