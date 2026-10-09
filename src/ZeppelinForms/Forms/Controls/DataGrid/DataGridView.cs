using System.Collections.ObjectModel;
using System.Collections.Specialized;
using ZeppelinForms.Core.Text;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Gestures;
using ZeppelinForms.Input.Mouse;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Forms.Controls.DataGrid;

/// <summary>A data table with row virtualization.</summary>
/// <remarks>
/// Cells are drawn rather than assembled from controls: twenty columns by thirty
/// visible rows would give six hundred elements going through measuring and layout
/// every frame. A control is materialized only for a cell being edited.
/// </remarks>
public partial class DataGridView : DecoratedControl, ITouchScrollTarget
{
    private const float ScrollBarThickness = 10f;

    /// <summary>A thumb shorter than this is hard to grab: a long table keeps it at
    /// this length, and the track's free part gets smaller instead.</summary>
    private const float MinThumbLength = 20f;

    private readonly List<float> _widths = [];

    private float _scrollX;
    private float _scrollY;

    /// <summary>Overscroll when scrolling with a finger. _scrollX and _scrollY don't
    /// include it — they are always within range — while rows and the header
    /// are drawn shifted by it.</summary>
    private Point _overscroll;

    private readonly TouchScroller _touch;

    /// <summary>The display order: row on screen → index in Items.
    /// null — as in the source. The collection itself is not touched: sorting
    /// a table is a way of looking at the data, not of changing it.</summary>
    private List<int>? _order;

    private int _resizingColumn = -1;
    private float _resizeStartX;
    private float _resizeStartWidth;

    /// <summary>The column's width definition before the drag: a cancelled
    /// resize puts it back.</summary>
    private GridLength _resizeStartDefinition;

    /// <summary>The press landed on a column boundary: that was a width drag,
    /// and turning it into a click on the header is not needed.</summary>
    private bool _suppressHeaderClick;

    /// <summary>A scrollbar thumb is held by the mouse.</summary>
    private bool _draggingVertical;
    private bool _draggingHorizontal;

    /// <summary>Where in the thumb it was grabbed: the thumb follows the cursor by
    /// that point rather than jumping its top edge to it.</summary>
    private float _dragOffset;

    /// <summary>The press landed on a scrollbar: the click it ends in is not a click
    /// on a row or a header.</summary>
    private bool _suppressClick;

    private int _hoveredRow = -1;

    /// <summary>The sortable header cell under the cursor, or −1.</summary>
    private int _hoveredHeader = -1;

    /// <summary>The selected record itself. The selection follows it rather than
    /// the row index: when Items change, the index may start pointing at another
    /// record, and a stale sort order may point past the end of the collection.</summary>
    private object? _selectedRecord;

    public List<DataGridViewColumn> Columns { get; init; } = [];

    /// <summary>Data rows. The type of the items is arbitrary — the columns know
    /// what to take out of them.</summary>
    public ObservableCollection<object> Items { get; } = [];

    /// <summary>Whether the table scrolls with a finger and a pen. Never with the mouse:
    /// on the desktop dragging with the mouse selects, and scrolling belongs to the wheel.</summary>
    public bool PanToScroll { get; set; } = true;

    public DataGridView()
    {
        Items.CollectionChanged += OnItemsChanged;

        // the grid scrolls by itself, bypassing PanelControl, so it needs
        // a scroll gesture of its own — but the physics is the same, shared
        _touch = TouchScroller.Attach(this);

        // the table takes the allotted space entirely: the centering inherited
        // from UnitControl would leave it a narrow strip in the middle of the page
        SetControlDefault(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
    }

    /// <remarks>
    /// The order and the selection are rebuilt from the record, not from the old
    /// index. Previously ApplySort read SelectedItem here — through the old order,
    /// whose indices no longer matched Items — and removing a row from a sorted
    /// table could throw ArgumentOutOfRangeException. Without sorting the selection
    /// quietly moved to a neighbouring record.
    /// </remarks>
    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // the display order was built for the previous contents: it holds
        // indices that no longer exist
        if (SortColumnIndex >= 0) BuildOrder();
        else _order = null;

        // the selected record may have moved or disappeared together with the data
        RestoreSelection(_selectedRecord);

        // EnsureLayout will clamp the offset, calling for layout is enough here
        Invalidate();
    }

    public float WheelStep { get; set; } = 48f;

    [Styled(Category = "DataGrid", AffectsLayout = true)]
    public partial float RowHeight { get; set; }
    private static float RowHeightDefault => 26f;

    [Styled(Category = "DataGrid", AffectsLayout = true)]
    public partial float HeaderHeight { get; set; }
    private static float HeaderHeightDefault => 30f;

    [Styled(Category = "DataGrid", AffectsLayout = true)]
    public partial Thickness CellPadding { get; set; }
    private static Thickness CellPaddingDefault => new(8, 4);

    [Styled(Category = "DataGrid")]
    public partial Color HeaderColor { get; set; }
    private static Color HeaderColorDefault => new(255, 244, 244, 244);

    [Styled(Category = "DataGrid")]
    public partial Color HeaderTextColor { get; set; }
    private static Color HeaderTextColorDefault => Colors.Black;

    [Styled(Category = "DataGrid")]
    public partial Color GridLineColor { get; set; }
    private static Color GridLineColorDefault => new(40, 0, 0, 0);

    [Styled(Category = "DataGrid")]
    public partial Color SelectionColor { get; set; }
    private static Color SelectionColorDefault => new(255, 205, 226, 252);

    /// <summary>The text of the selected row. Transparent — the ordinary text
    /// color; a contrast theme pairs its own with the selection fill.</summary>
    [Styled(Category = "DataGrid")]
    public partial Color SelectedTextColor { get; set; }
    private static Color SelectedTextColorDefault => Colors.Transparent;

    [Styled(Category = "DataGrid")]
    public partial Color RowHoverColor { get; set; }
    private static Color RowHoverColorDefault => new(20, 0, 0, 0);

    /// <summary>The header cell under the cursor, when a click on it sorts.</summary>
    [Styled(Category = "DataGrid")]
    public partial Color HeaderHoverColor { get; set; }
    private static Color HeaderHoverColorDefault => new(255, 232, 232, 232);

    [Styled(Category = "Scrolling")]
    public partial Color ScrollTrackColor { get; set; }
    private static Color ScrollTrackColorDefault => new(40, 0, 0, 0);

    [Styled(Category = "Scrolling")]
    public partial Color ScrollThumbColor { get; set; }
    private static Color ScrollThumbColorDefault => new(120, 0, 0, 0);

    public int SelectedIndex
    {
        get;
        set
        {
            int clamped = value < 0 || value >= Items.Count ? -1 : value;

            // the record is remembered on every assignment, even a repeated one:
            // it is what the selection is restored by after the data changes
            _selectedRecord = clamped >= 0 ? RowItem(clamped) : null;

            if (field == clamped) return;

            field = clamped;

            SelectionChanged?.Invoke(this, SelectedItem);
            InvalidateVisual();
        }
    } = -1;

    public object? SelectedItem => SelectedIndex >= 0 ? RowItem(SelectedIndex) : null;

    /// <summary>The data of a row by its place on screen. With sorting this
    /// is not the same as Items[row].</summary>
    public object RowItem(int row) => Items[_order is null ? row : _order[row]];

    // ===== sorting =====

    /// <summary>The column the table is sorted by. −1 — the source's order.</summary>
    public int SortColumnIndex { get; private set; } = -1;

    public bool SortDescending { get; private set; }

    /// <summary>Allow sorting by a click on the header. A column itself
    /// may refuse through CanSort.</summary>
    public bool CanSortByHeaderClick { get; set; } = true;

    public event EventHandler? SortChanged;

    /// <summary>Sort by a column. Without an explicit direction, a repeated call
    /// for the same column reverses the order — that is how a click on the header behaves.</summary>
    public void SortBy(int columnIndex, bool? descending = null)
    {
        if (columnIndex < 0 || columnIndex >= Columns.Count)
        {
            ClearSort();
            return;
        }

        SortDescending = descending ?? (SortColumnIndex == columnIndex && !SortDescending);
        SortColumnIndex = columnIndex;

        ApplySort();

        SortChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Return to the source's order.</summary>
    public void ClearSort()
    {
        if (SortColumnIndex < 0) return;

        SortColumnIndex = -1;
        SortDescending = false;

        ApplySort();

        SortChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplySort()
    {
        BuildOrder();

        // the selection holds on to the data record, not to its place on screen:
        // after sorting the same record must stay selected
        RestoreSelection(_selectedRecord);

        InvalidateVisual();
    }

    /// <summary>Build the display order for the current contents and sort column.</summary>
    private void BuildOrder()
    {
        if (SortColumnIndex < 0 || SortColumnIndex >= Columns.Count)
        {
            _order = null;
            return;
        }

        DataGridViewColumn column = Columns[SortColumnIndex];
        IComparer<object?> comparer = column.Comparer ?? Comparer<object?>.Default;

        var order = new List<int>(Items.Count);

        for (int i = 0; i < Items.Count; i++) order.Add(i);

        order.Sort((left, right) =>
        {
            int result = comparer.Compare(column.Value(Items[left]), column.Value(Items[right]));

            // List.Sort is unstable: without this, equal values swap places
            // from call to call, and rows jump before the eyes
            if (result == 0) return left.CompareTo(right);

            return SortDescending ? -result : result;
        });

        _order = order;
    }

    private void RestoreSelection(object? selected)
    {
        if (selected is null)
        {
            SelectedIndex = -1;
            return;
        }

        for (int row = 0; row < Items.Count; row++)
        {
            if (!ReferenceEquals(RowItem(row), selected)) continue;

            SelectedIndex = row;
            return;
        }

        SelectedIndex = -1;
    }

    public event EventHandler<object?>? SelectionChanged;

    // ===== geometry =====

    private bool _verticalBar;
    private bool _horizontalBar;
    private int _visibleFirst;
    private int _visibleLast = -1;

    /// <summary>The rows area: the content minus the header and the bars.</summary>
    private Rectangle BodyBounds
    {
        get
        {
            Rectangle content = this.ContentBounds;

            return new Rectangle(
                new Point(content.X, content.Y + HeaderHeight),
                new Size(
                    Math.Max(0, content.Width - (_verticalBar ? ScrollBarThickness : 0)),
                    Math.Max(0, content.Height - HeaderHeight - (_horizontalBar ? ScrollBarThickness : 0))));
        }
    }

    private float TotalWidth
    {
        get
        {
            float total = 0;

            foreach (float width in _widths) total += width;

            return total;
        }
    }

    private float TotalHeight => Items.Count * RowHeight;

    private float MaxScrollX => Math.Max(0, TotalWidth - BodyBounds.Width);

    private float MaxScrollY => Math.Max(0, TotalHeight - BodyBounds.Height);

    /// <summary>Recompute the visible range, the bars and the column widths.</summary>
    /// <remarks>
    /// Called from everywhere the geometry is needed, not only from drawing:
    /// otherwise hit testing and ScrollTo would work on the previous frame's data,
    /// and before the first frame — on empty data.
    ///
    /// Three quantities depend on each other in a circle: Auto widths need the
    /// visible range, the decision about the horizontal bar needs the widths, the
    /// body height needs the bar, and the visible range needs the body height.
    /// The circle is broken by a pessimistic estimate of the range: we assume the
    /// horizontal bar is always there. The cost of the mistake is one extra row
    /// in computing an Auto width, and that one is on the safe side.
    /// </remarks>
    private void EnsureLayout()
    {
        Rectangle content = this.ContentBounds;

        float bodyHeightGuess = Math.Max(0, content.Height - HeaderHeight - ScrollBarThickness);

        _visibleFirst = RowHeight <= 0 ? 0 : Math.Max(0, (int)(_scrollY / RowHeight));
        _visibleLast = RowHeight <= 0
            ? -1
            : Math.Min(Items.Count - 1, (int)((_scrollY + bodyHeightGuess) / RowHeight));

        // two passes: the bars take space from each other, and one isn't enough
        _verticalBar = TotalHeight > content.Height - HeaderHeight;

        float available = content.Width - (_verticalBar ? ScrollBarThickness : 0);

        ResolveWidths(available, _visibleFirst, _visibleLast);

        _horizontalBar = TotalWidth > available;

        if (_horizontalBar && !_verticalBar)
        {
            _verticalBar = TotalHeight > content.Height - HeaderHeight - ScrollBarThickness;

            if (_verticalBar)
                ResolveWidths(content.Width - ScrollBarThickness, _visibleFirst, _visibleLast);
        }

        // the content may have shrunk — the offset must stay within range
        _scrollX = Math.Clamp(_scrollX, 0, MaxScrollX);
        _scrollY = Math.Clamp(_scrollY, 0, MaxScrollY);
    }

    /// <summary>Distributes the widths: first fixed and Auto,
    /// the remainder is shared among the star columns.</summary>
    private void ResolveWidths(float available, int firstRow, int lastRow)
    {
        _widths.Clear();

        float taken = 0;
        float starWeight = 0;

        foreach (DataGridViewColumn column in Columns)
        {
            if (column.Width.IsStar)
            {
                starWeight += column.Width.Value;
                _widths.Add(0);
                continue;
            }

            float width = column.Width.IsAuto
                ? MeasureAuto(column, firstRow, lastRow)
                : column.Width.Value;

            _widths.Add(width);
            taken += width;
        }

        if (starWeight <= 0) return;

        float rest = Math.Max(0, available - taken);

        for (int i = 0; i < Columns.Count; i++)
        {
            if (!Columns[i].Width.IsStar) continue;

            _widths[i] = rest * (Columns[i].Width.Value / starWeight);
        }
    }

    /// <summary>The width by the header and the visible rows — not by all of them.</summary>
    private float MeasureAuto(DataGridViewColumn column, int firstRow, int lastRow)
    {
        Font font = this.EffectiveFont;

        float width = string.IsNullOrEmpty(column.Header)
            ? 0
            : TextMeasurer.Current.MeasureText(column.Header, font).Width;

        for (int i = firstRow; i <= lastRow && i < Items.Count; i++)
        {
            float cell = TextMeasurer.Current.MeasureText(column.TextOf(RowItem(i)), font).Width;

            if (cell > width) width = cell;
        }

        return width + CellPadding.Horizontal;
    }

    // ===== scrolling =====

    public void ScrollTo(float x, float y)
    {
        // an explicit scroll from code or the wheel takes priority over fling inertia
        _touch.Stop();

        EnsureLayout();

        float clampedX = Math.Clamp(x, 0, MaxScrollX);
        float clampedY = Math.Clamp(y, 0, MaxScrollY);

        if (clampedX == _scrollX && clampedY == _scrollY) return;

        _scrollX = clampedX;
        _scrollY = clampedY;

        InvalidateVisual();
    }

    /// <summary>A row's band in the form's coordinates, scrolled as drawn — for the
    /// accessibility peer, whose rows are not elements. Outside the visible part the
    /// band lies outside the body too; the caller clips it.</summary>
    internal Rectangle RowBounds(int rowIndex)
    {
        // asked before the first frame, the bars aren't decided yet: the body
        // would come out as wide as the whole grid
        EnsureLayout();

        Rectangle body = BodyBounds;
        Point origin = GetAbsolutePosition();

        return new Rectangle(
            new Point(origin.X + body.X, origin.Y + body.Y + rowIndex * RowHeight - _scrollY),
            new Size(body.Width, RowHeight));
    }

    /// <summary>The body in the form's coordinates: what is visible of the rows.</summary>
    internal Rectangle VisibleBodyBounds
    {
        get
        {
            EnsureLayout();

            Rectangle body = BodyBounds;
            Point origin = GetAbsolutePosition();

            return new Rectangle(new Point(origin.X + body.X, origin.Y + body.Y), body.Size);
        }
    }

    /// <summary>Bring a row into the visible part.</summary>
    public void ScrollIntoView(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= Items.Count) return;

        float top = rowIndex * RowHeight;
        float bottom = top + RowHeight;

        if (top < _scrollY) ScrollTo(_scrollX, top);
        else if (bottom > _scrollY + BodyBounds.Height) ScrollTo(_scrollX, bottom - BodyBounds.Height);
    }

    UIElement ITouchScrollTarget.Element => this;

    bool ITouchScrollTarget.CanPanHorizontally
    {
        get
        {
            EnsureLayout();

            return PanToScroll && MaxScrollX > 0;
        }
    }

    bool ITouchScrollTarget.CanPanVertically
    {
        get
        {
            EnsureLayout();

            return PanToScroll && MaxScrollY > 0;
        }
    }

    Size ITouchScrollTarget.PanViewport => BodyBounds.Size;

    Point ITouchScrollTarget.PanScroll => new(_scrollX, _scrollY);

    Point ITouchScrollTarget.PanMaxScroll => new(MaxScrollX, MaxScrollY);

    void ITouchScrollTarget.ApplyPanScroll(Point scroll, Point overscroll)
    {
        _scrollX = scroll.X;
        _scrollY = scroll.Y;
        _overscroll = overscroll;

        // the visible row range is computed from _scrollY, so recomputing the
        // geometry is mandatory — otherwise a fling would leave empty strips at the bottom
        EnsureLayout();

        InvalidateVisual();
    }

    protected override void OnPointerDown(PointerEventArgs e)
    {
        if (e.Kind != PointerKind.Mouse) _touch.StopFling();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        // the form delivers the wheel to disabled elements too
        if (!IsEnabled) return;

        float before = _scrollY;

        ScrollTo(_scrollX, _scrollY - (e.Delta / 120f * WheelStep));

        // the event is marked handled only if we actually moved: otherwise a grid
        // scrolled to the end would eat the wheel from its parent
        if (_scrollY != before) e.Handled = true;
    }

    // ===== input =====

    private int RowAt(Point location)
    {
        EnsureLayout();

        Point abs = GetAbsolutePosition();
        float localX = location.X - abs.X - Padding.Left;
        float localY = location.Y - abs.Y - Padding.Top;

        Rectangle body = BodyBounds;
        float bodyTop = HeaderHeight;

        if (localY < bodyTop || localY > bodyTop + body.Height) return -1;

        // the vertical bar lies over the right edge of the rows: a press on it is
        // not a press on the row behind it
        if (localX < 0 || localX > body.Width) return -1;

        int index = (int)((localY - bodyTop + _scrollY + _overscroll.Y) / RowHeight);

        return index >= 0 && index < Items.Count ? index : -1;
    }

    /// <summary>Whether column boundaries can be dragged with the mouse.</summary>
    public bool CanResizeColumns { get; set; } = true;

    /// <summary>A column can't be made narrower than this: otherwise it is easy
    /// to lose it completely, and there would be nothing to grab to bring it back.</summary>
    public float MinColumnWidth { get; set; } = 32f;

    /// <summary>How close to a boundary the cursor must be to grab it.
    /// Nobody hits exactly on the line.</summary>
    private const float ResizeGrip = 4f;

    /// <summary>A point in the control's own coordinates, without the padding.</summary>
    private Point ToLocal(Point location)
    {
        Point abs = GetAbsolutePosition();

        return new Point(location.X - abs.X - Padding.Left, location.Y - abs.Y - Padding.Top);
    }

    private bool IsOverHeader(Point local) => local.Y >= 0 && local.Y < HeaderHeight;

    /// <summary>The column whose right boundary the cursor holds, or −1.</summary>
    private int ColumnEdgeAt(Point local)
    {
        if (!CanResizeColumns || !IsOverHeader(local)) return -1;

        EnsureLayout();

        // the header moves horizontally together with the body, so the
        // boundaries are counted from the same offset it is drawn with
        float x = -_scrollX - _overscroll.X;

        for (int col = 0; col < _widths.Count; col++)
        {
            x += _widths[col];

            if (MathF.Abs(local.X - x) <= ResizeGrip) return col;
        }

        return -1;
    }

    private int ColumnAt(Point local)
    {
        EnsureLayout();

        float x = -_scrollX - _overscroll.X;

        for (int col = 0; col < _widths.Count; col++)
        {
            if (local.X >= x && local.X < x + _widths[col]) return col;

            x += _widths[col];
        }

        return -1;
    }

    /// <summary>The column under the point if clicking its header sorts, otherwise −1.
    /// The same conditions as in OnClick, so the highlight never promises
    /// a sort that won't happen.</summary>
    private int SortableColumnAt(Point local)
    {
        if (!CanSortByHeaderClick) return -1;

        int column = ColumnAt(local);

        return column >= 0 && Columns[column].CanSort ? column : -1;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        _suppressHeaderClick = false;
        _suppressClick = false;

        // only the left button resizes and scrolls: the right one belongs to the context menu
        if (e.Button != MouseButton.Left) return;

        if (BeginScrollBarPress(e.Location))
        {
            _suppressClick = true;
            return;
        }

        int edge = ColumnEdgeAt(ToLocal(e.Location));
        if (edge < 0) return;

        _resizingColumn = edge;
        _resizeStartX = e.Location.X;
        _resizeStartWidth = _widths[edge];
        _resizeStartDefinition = Columns[edge].Width;
        _suppressHeaderClick = true;

        // without capture the drag breaks off as soon as the cursor leaves the window
        CaptureMouse();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (EndThumbDrag()) return;

        if (_resizingColumn < 0) return;

        _resizingColumn = -1;
        ReleaseMouseCapture();
    }

    /// <summary>The interaction was cut off — the system took the capture away.
    /// Previously _resizingColumn stayed set, and after that simply moving the mouse
    /// with no button pressed kept resizing the column. Nothing was committed,
    /// so the column gets back the width it had before the press.</summary>
    protected override void OnPointerCanceled(PointerCancelEventArgs e)
    {
        // there will be no release after a cancel: without a reset the next mouse
        // move with no button pressed would keep dragging the thumb
        if (EndThumbDrag()) return;

        if (_resizingColumn < 0) return;

        if (_resizingColumn < Columns.Count)
            Columns[_resizingColumn].Width = _resizeStartDefinition;

        _resizingColumn = -1;
        Invalidate();
    }

    protected override void OnClick(MouseClickEventArgs e)
    {
        // the press paged or dragged a scrollbar
        if (_suppressClick) return;

        Point local = ToLocal(e.Location);

        if (IsOverHeader(local))
        {
            // the click that dragged a boundary doesn't count as sorting
            if (_suppressHeaderClick) return;

            int column = SortableColumnAt(local);

            if (column >= 0)
            {
                SortBy(column);
                e.Handled = true;
            }

            return;
        }

        int row = RowAt(e.Location);
        if (row < 0) return;

        SelectedIndex = row;
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        if (DragThumb(e.Location)) return;

        if (_resizingColumn >= 0)
        {
            // the width is set as fixed: dragging a star or an Auto makes
            // no sense — the very next recompute would bring the old one back
            Columns[_resizingColumn].Width = GridLength.Fixed(
                Math.Max(MinColumnWidth, _resizeStartWidth + (e.Location.X - _resizeStartX)));

            Invalidate();
            return;
        }

        Point local = ToLocal(e.Location);
        bool onEdge = ColumnEdgeAt(local) >= 0;

        Cursor = onEdge ? CursorKind.SizeWestEast : CursorKind.Default;

        // a header cell lights up only when a click on it would sort: the highlight
        // promises an action, and a column that can't sort has none. Not on
        // a resize edge either — a click there resizes rather than sorts
        int header = !onEdge && IsOverHeader(local) ? SortableColumnAt(local) : -1;
        int row = RowAt(e.Location);

        if (row == _hoveredRow && header == _hoveredHeader) return;

        _hoveredRow = row;
        _hoveredHeader = header;
        InvalidateVisual();
    }

    protected override void OnMouseExit(MouseMoveEventArgs e)
    {
        if (_hoveredRow < 0 && _hoveredHeader < 0) return;

        _hoveredRow = -1;
        _hoveredHeader = -1;
        InvalidateVisual();
    }

    // ===== scrollbar input =====

    /// <summary>The vertical track in the element's own coordinates: along the
    /// rows, at the right edge.</summary>
    private Rectangle VerticalTrack
    {
        get
        {
            Rectangle content = this.ContentBounds;
            Rectangle body = BodyBounds;

            return new Rectangle(
                new Point(content.X + content.Width - ScrollBarThickness, body.Y),
                new Size(ScrollBarThickness, body.Height));
        }
    }

    /// <summary>The horizontal track: under the rows, as wide as they are.</summary>
    private Rectangle HorizontalTrack
    {
        get
        {
            Rectangle content = this.ContentBounds;

            return new Rectangle(
                new Point(content.X, content.Y + content.Height - ScrollBarThickness),
                new Size(BodyBounds.Width, ScrollBarThickness));
        }
    }

    /// <summary>The thumb along the vertical track. One computation for drawing and
    /// for hit testing: otherwise the thumb is drawn in one place and grabbed in another.</summary>
    private (float Position, float Length) VerticalThumb => Thumb(VerticalTrack.Height, TotalHeight, _scrollY, MaxScrollY);

    private (float Position, float Length) HorizontalThumb => Thumb(HorizontalTrack.Width, TotalWidth, _scrollX, MaxScrollX);

    private static (float Position, float Length) Thumb(float track, float total, float scroll, float maxScroll)
    {
        float length = total <= 0 ? track : Math.Min(track, Math.Max(MinThumbLength, track * (track / total)));
        float position = maxScroll <= 0 ? 0 : (track - length) * (scroll / maxScroll);

        return (position, length);
    }

    private static bool Contains(Rectangle r, Point p) =>
        p.X >= r.X && p.X <= r.X + r.Width && p.Y >= r.Y && p.Y <= r.Y + r.Height;

    /// <summary>A press on a scrollbar: the thumb is grabbed, the track around it
    /// pages. False — the press is not on a bar.</summary>
    /// <remarks>
    /// The bars used to be drawn only: a press on the vertical one went to the
    /// row behind it and selected that row, and the thumb could not be dragged.
    /// </remarks>
    private bool BeginScrollBarPress(Point location)
    {
        EnsureLayout();

        Point abs = GetAbsolutePosition();
        var local = new Point(location.X - abs.X, location.Y - abs.Y);

        if (_verticalBar && Contains(VerticalTrack, local))
        {
            Rectangle track = VerticalTrack;
            var (position, length) = VerticalThumb;
            float offset = local.Y - track.Y;

            if (offset >= position && offset <= position + length)
            {
                _draggingVertical = true;
                _dragOffset = offset - position;

                // without capture the drag breaks off as soon as the cursor leaves the window
                CaptureMouse();
            }
            else
            {
                float page = BodyBounds.Height;
                ScrollTo(_scrollX, _scrollY + (offset < position ? -page : page));
            }

            return true;
        }

        if (_horizontalBar && Contains(HorizontalTrack, local))
        {
            Rectangle track = HorizontalTrack;
            var (position, length) = HorizontalThumb;
            float offset = local.X - track.X;

            if (offset >= position && offset <= position + length)
            {
                _draggingHorizontal = true;
                _dragOffset = offset - position;
                CaptureMouse();
            }
            else
            {
                float page = BodyBounds.Width;
                ScrollTo(_scrollX + (offset < position ? -page : page), _scrollY);
            }

            return true;
        }

        return false;
    }

    /// <summary>Move the held thumb after the cursor. False — no thumb is held.</summary>
    private bool DragThumb(Point location)
    {
        if (!_draggingVertical && !_draggingHorizontal) return false;

        EnsureLayout();

        Point abs = GetAbsolutePosition();

        if (_draggingVertical)
        {
            Rectangle track = VerticalTrack;
            float free = track.Height - VerticalThumb.Length;

            if (free > 0)
            {
                float t = (location.Y - abs.Y - track.Y - _dragOffset) / free;
                ScrollTo(_scrollX, MaxScrollY * Math.Clamp(t, 0, 1));
            }
        }
        else
        {
            Rectangle track = HorizontalTrack;
            float free = track.Width - HorizontalThumb.Length;

            if (free > 0)
            {
                float t = (location.X - abs.X - track.X - _dragOffset) / free;
                ScrollTo(MaxScrollX * Math.Clamp(t, 0, 1), _scrollY);
            }
        }

        return true;
    }

    /// <summary>Let the thumb go. False — none was held.</summary>
    private bool EndThumbDrag()
    {
        if (!_draggingVertical && !_draggingHorizontal) return false;

        _draggingVertical = _draggingHorizontal = false;
        ReleaseMouseCapture();

        return true;
    }

    // ===== drawing =====

    protected override void DrawContent(Graphics g)
    {
        EnsureLayout();

        Rectangle content = this.ContentBounds;
        Rectangle body = BodyBounds;
        Font font = this.EffectiveFont;

        DrawRows(g, body, _visibleFirst, _visibleLast, font);
        DrawHeader(g, content, font);
        DrawScrollBars(g);
    }

    private void DrawRows(Graphics g, Rectangle body, int first, int last, Font font)
    {
        g.Save();
        g.ClipRect(body);

        for (int row = first; row <= last; row++)
        {
            float top = body.Y + (row * RowHeight) - _scrollY - _overscroll.Y;
            var rowRect = new Rectangle(new Point(body.X, top), new Size(TotalWidth, RowHeight));

            if (row == SelectedIndex) g.FillRectangle(rowRect, SelectionColor);
            else if (row == _hoveredRow) g.FillRectangle(rowRect, RowHoverColor);

            // the keyboard's row: an outline inside the band, over the selection
            // fill, while focus is visible
            if (row == SelectedIndex && IsFocusVisible)
            {
                var outline = new Rectangle(
                    new Point(body.X + 1f, top + 1f),
                    new Size(Math.Max(0, body.Width - 2f), Math.Max(0, RowHeight - 2f)));

                g.DrawRectangle(outline, TextColor, 1f);
            }

            float x = body.X - _scrollX - _overscroll.X;

            for (int col = 0; col < Columns.Count; col++)
            {
                var cell = new Rectangle(
                    new Point(x + CellPadding.Left, top + CellPadding.Top),
                    new Size(
                        Math.Max(0, _widths[col] - CellPadding.Horizontal),
                        Math.Max(0, RowHeight - CellPadding.Vertical)));

                // the selected row's text in its own color where the theme pairs one
                // with the selection fill — high contrast does
                Color text = row == SelectedIndex && SelectedTextColor.A > 0 ? SelectedTextColor : TextColor;

                g.DrawText(Columns[col].TextOf(RowItem(row)), cell, text, font,
                    Columns[col].Align, VerticalContentAlignment.Center);

                x += _widths[col];
            }

            g.DrawLine(
                new Point(body.X, top + RowHeight),
                new Point(body.X + body.Width, top + RowHeight),
                GridLineColor, 1f);
        }

        g.Restore();
    }

    private void DrawHeader(Graphics g, Rectangle content, Font font)
    {
        var headerRect = new Rectangle(
            new Point(content.X, content.Y), new Size(content.Width, HeaderHeight));

        g.FillRectangle(headerRect, HeaderColor);

        // the header moves horizontally together with the body, but not vertically —
        // that is why the grid keeps the offsets itself rather than inheriting PanelControl
        g.Save();
        g.ClipRect(headerRect);

        float x = content.X - _scrollX - _overscroll.X;

        for (int col = 0; col < Columns.Count; col++)
        {
            var cell = new Rectangle(
                new Point(x + CellPadding.Left, content.Y),
                new Size(Math.Max(0, _widths[col] - CellPadding.Horizontal), HeaderHeight));

            // the whole column width, not the padded cell: the click area is the column
            if (col == _hoveredHeader)
                g.FillRectangle(
                    new Rectangle(new Point(x, content.Y), new Size(_widths[col], HeaderHeight)),
                    HeaderHoverColor);

            if (col == SortColumnIndex) DrawSortMarker(g, cell);

            g.DrawText(Columns[col].Header ?? string.Empty, cell, HeaderTextColor, font,
                Columns[col].Align, VerticalContentAlignment.Center);

            x += _widths[col];
        }

        g.Restore();

        g.DrawLine(
            new Point(content.X, content.Y + HeaderHeight),
            new Point(content.X + content.Width, content.Y + HeaderHeight),
            GridLineColor, 1f);
    }

    /// <summary>The sort direction triangle at the right edge of the header cell.
    /// No space is reserved for it: there are usually few columns, and taking
    /// width from all of them for the sake of one is worse than occasionally
    /// overlaying the mark on a long header.</summary>
    private void DrawSortMarker(Graphics g, Rectangle cell)
    {
        const float half = 4f;

        float centerX = cell.X + cell.Width - half;
        float centerY = cell.Y + cell.Height / 2f;
        float direction = SortDescending ? 1f : -1f;

        Span<Point> triangle =
        [
            new Point(centerX - half, centerY - half / 2f * direction),
            new Point(centerX + half, centerY - half / 2f * direction),
            new Point(centerX, centerY + half * direction),
        ];

        g.FillPolygon(triangle, HeaderTextColor);
    }

    private void DrawScrollBars(Graphics g)
    {
        if (_verticalBar)
        {
            Rectangle track = VerticalTrack;
            var (position, length) = VerticalThumb;

            g.FillRoundRectangle(track, new CornerRadius(ScrollBarThickness / 2f), ScrollTrackColor);
            g.FillRoundRectangle(
                new Rectangle(
                    new Point(track.X + 2, track.Y + position),
                    new Size(ScrollBarThickness - 4, length)),
                new CornerRadius((ScrollBarThickness - 4) / 2f), ScrollThumbColor);
        }

        if (!_horizontalBar) return;

        Rectangle trackH = HorizontalTrack;
        var (positionH, lengthH) = HorizontalThumb;

        g.FillRoundRectangle(trackH, new CornerRadius(ScrollBarThickness / 2f), ScrollTrackColor);
        g.FillRoundRectangle(
            new Rectangle(
                new Point(trackH.X + positionH, trackH.Y + 2),
                new Size(lengthH, ScrollBarThickness - 4)),
            new CornerRadius((ScrollBarThickness - 4) / 2f), ScrollThumbColor);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        ResolveSize(
            new Size(
                Columns.Count * 100f,
                HeaderHeight + (Math.Min(Items.Count, 10) * RowHeight)),
            availableSize);
}