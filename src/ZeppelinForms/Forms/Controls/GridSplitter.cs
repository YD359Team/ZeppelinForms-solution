using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Разделитель внутри Grid: перетаскивание меняет размеры соседних треков.
/// Помещается в собственную ячейку между изменяемыми.
/// </summary>
public class GridSplitter : UnitControl
{
    private bool _dragging;
    private float _dragStart;
    private float _beforeStart;
    private float _afterStart;

    public Orientation Orientation { get; set; } = Orientation.Vertical;

    public float MinTrackSize { get; set; } = 30f;

    public Color LineColor { get; set; } = new Color(255, 214, 214, 214);
    public Color HoverColor { get; set; } = new Color(255, 170, 170, 170);

    private bool IsVertical => Orientation == Orientation.Vertical;

    public GridSplitter()
    {
        Size = IsVertical ? new Size(6, float.NaN) : new Size(float.NaN, 6);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
    }

    protected override void OnAttached()
    {
        Cursor = IsVertical ? CursorKind.SizeWestEast : CursorKind.SizeNorthSouth;
    }

    public override void Draw(Graphics g)
    {
        g.FillRectangle(LocalBounds, IsHovered || _dragging ? HoverColor : LineColor);
    }

    private Grid? ParentGrid => Parent as Grid;

    /// <summary>Индексы треков слева/сверху и справа/снизу от разделителя.</summary>
    private (int Before, int After) Neighbours =>
        IsVertical ? (Column - 1, Column + 1) : (Row - 1, Row + 1);

    protected override void OnMouseDown(MouseButtonEventArgs args)
    {
        Grid? grid = ParentGrid;
        if (grid is null) return;

        var (before, after) = Neighbours;

        List<GridLength> definitions = IsVertical ? grid.ColumnDefinitions : grid.RowDefinitions;

        if (before < 0 || after >= definitions.Count) return;

        _dragging = true;
        _dragStart = IsVertical ? args.Location.X : args.Location.Y;

        // фиксируем стартовые размеры: считать от текущих на каждом шаге
        // нельзя — накопится дрейф
        _beforeStart = MeasuredTrackSize(grid, before);
        _afterStart = MeasuredTrackSize(grid, after);
    }

    protected override void OnMouseExit(MouseMoveEventArgs args)
    {
        if (!_dragging) return;

        Grid? grid = ParentGrid;
        if (grid is null) return;

        var (before, after) = Neighbours;

        float delta = (IsVertical ? args.Location.X : args.Location.Y) - _dragStart;

        float newBefore = _beforeStart + delta;
        float newAfter = _afterStart - delta;

        if (newBefore < MinTrackSize || newAfter < MinTrackSize) return;

        List<GridLength> definitions = IsVertical ? grid.ColumnDefinitions : grid.RowDefinitions;

        // фиксированные размеры вместо звёзд: после перетаскивания
        // пользователь задал конкретную пропорцию, и она не должна плыть
        definitions[before] = GridLength.Fixed(newBefore);
        definitions[after] = GridLength.Fixed(newAfter);

        grid.Invalidate();
    }

    protected override void OnMouseUp(MouseButtonEventArgs args) => _dragging = false;

    private float MeasuredTrackSize(Grid grid, int index)
    {
        // берём фактический размер соседнего элемента в том же треке
        foreach (UIElement child in grid.Children)
        {
            if (ReferenceEquals(child, this)) continue;

            int track = IsVertical ? child.Column : child.Row;
            if (track != index) continue;

            return IsVertical ? child.ActualSize.Width : child.ActualSize.Height;
        }

        List<GridLength> definitions = IsVertical ? grid.ColumnDefinitions : grid.RowDefinitions;

        return index >= 0 && index < definitions.Count && !definitions[index].IsStar
            ? definitions[index].Value
            : MinTrackSize;
    }

    protected override Size MeasureOverride(Size availableSize) =>
        ResolveSize(IsVertical ? new Size(6, 0) : new Size(0, 6), availableSize);
}