using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Mouse;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// A splitter inside a Grid: dragging it changes the sizes of the neighbouring tracks.
/// Placed in its own cell between the resizable ones.
/// </summary>
public partial class GridSplitter : DecoratedControl
{
    private const float Thickness = 6f;

    private bool _dragging;
    private float _dragStart;
    private float _beforeStart;
    private float _afterStart;

    // the definitions as they were before the press: a cancelled drag
    // puts them back instead of leaving the tracks halfway
    private GridLength _beforeDefinition;
    private GridLength _afterDefinition;

    /// <remarks>
    /// The thickness comes from MeasureOverride, not from Size set in the
    /// constructor: the constructor runs before an object initializer, and
    /// <c>new GridSplitter { Orientation = Orientation.Horizontal }</c> used to
    /// keep the size of a vertical splitter.
    /// </remarks>
    public Orientation Orientation
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            Cursor = CursorFor(value);
            Invalidate();
        }
    } = Orientation.Vertical;

    public float MinTrackSize { get; set; } = 30f;

    [Styled(Category = "Splitter")]
    public partial Color LineColor { get; set; }
    private static Color LineColorDefault => new(255, 214, 214, 214);

    [Styled(Category = "Splitter")]
    public partial Color HoverColor { get; set; }
    private static Color HoverColorDefault => new(255, 170, 170, 170);

    private bool IsVertical => Orientation == Orientation.Vertical;

    public GridSplitter()
    {
        Cursor = CursorFor(Orientation);
        SetControlDefault(HorizontalAlignmentProperty, Enums.HorizontalAlignment.Stretch);
        SetControlDefault(VerticalAlignmentProperty, Enums.VerticalAlignment.Stretch);
    }

    private static CursorKind CursorFor(Orientation orientation) =>
        orientation == Orientation.Vertical ? CursorKind.SizeWestEast : CursorKind.SizeNorthSouth;

    protected override void OnAttached()
    {
        Cursor = IsVertical ? CursorKind.SizeWestEast : CursorKind.SizeNorthSouth;
    }

    protected override void DrawContent(Graphics g)
    {
        g.FillRectangle(LocalBounds, IsHovered || _dragging ? HoverColor : LineColor);
    }

    private Grid? ParentGrid => Parent as Grid;

    /// <summary>Indices of the tracks left/above and right/below the splitter.</summary>
    private (int Before, int After) Neighbours =>
        IsVertical ? (Column - 1, Column + 1) : (Row - 1, Row + 1);

    private List<GridLength> Definitions(Grid grid) =>
        IsVertical ? grid.ColumnDefinitions : grid.RowDefinitions;

    protected override void OnMouseDown(MouseButtonEventArgs args)
    {
        // only the left button drags: the right one belongs to the context menu
        if (args.Button != MouseButton.Left) return;

        Grid? grid = ParentGrid;
        if (grid is null) return;

        var (before, after) = Neighbours;

        List<GridLength> definitions = Definitions(grid);

        if (before < 0 || after >= definitions.Count) return;

        _dragging = true;
        _dragStart = IsVertical ? args.Location.X : args.Location.Y;

        // record the starting sizes: computing from the current ones
        // at every step is not allowed — drift would accumulate
        _beforeStart = MeasuredTrackSize(grid, before);
        _afterStart = MeasuredTrackSize(grid, after);

        _beforeDefinition = definitions[before];
        _afterDefinition = definitions[after];

        // without capture the drag breaks off as soon as the cursor
        // leaves the window
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseMoveEventArgs args)
    {
        if (!_dragging) return;

        Grid? grid = ParentGrid;
        if (grid is null) return;

        var (before, after) = Neighbours;

        float delta = (IsVertical ? args.Location.X : args.Location.Y) - _dragStart;

        float newBefore = _beforeStart + delta;
        float newAfter = _afterStart - delta;

        if (newBefore < MinTrackSize || newAfter < MinTrackSize) return;

        List<GridLength> definitions = Definitions(grid);

        // fixed sizes instead of stars: after dragging the user
        // has set a specific proportion, and it must not drift
        definitions[before] = GridLength.Fixed(newBefore);
        definitions[after] = GridLength.Fixed(newAfter);

        grid.Invalidate();
    }

    protected override void OnMouseUp(MouseButtonEventArgs args)
    {
        if (!_dragging) return;

        _dragging = false;

        ReleaseMouseCapture();
        InvalidateVisual();
    }

    /// <summary>The interaction was cut off — the system took the capture away,
    /// the splitter left the tree. Previously _dragging stayed true here, and
    /// after that simply hovering over the splitter with no button pressed
    /// kept resizing the tracks.</summary>
    protected override void OnPointerCanceled(PointerCancelEventArgs e)
    {
        if (!_dragging) return;

        _dragging = false;

        // nothing was committed: the tracks go back to what they were before the press
        if (ParentGrid is { } grid)
        {
            var (before, after) = Neighbours;
            List<GridLength> definitions = Definitions(grid);

            if (before >= 0 && after < definitions.Count)
            {
                definitions[before] = _beforeDefinition;
                definitions[after] = _afterDefinition;

                grid.Invalidate();
            }
        }

        InvalidateVisual();
    }

    private float MeasuredTrackSize(Grid grid, int index)
    {
        // take the actual size of a neighbouring element in the same track
        foreach (UIElement child in grid.Children)
        {
            if (ReferenceEquals(child, this)) continue;

            int track = IsVertical ? child.Column : child.Row;
            if (track != index) continue;

            return IsVertical ? child.ActualSize.Width : child.ActualSize.Height;
        }

        List<GridLength> definitions = Definitions(grid);

        return index >= 0 && index < definitions.Count && !definitions[index].IsStar
            ? definitions[index].Value
            : MinTrackSize;
    }

    protected override Size MeasureOverride(Size availableSize) =>
        ResolveSize(IsVertical ? new Size(Thickness, 0) : new Size(0, Thickness), availableSize);
}