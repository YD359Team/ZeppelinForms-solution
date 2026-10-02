using System.Collections.Specialized;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls.DataGrid;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Accessibility.Peers;

/// <summary>A data grid: a header row of column headers, then the data rows.</summary>
/// <remarks>
/// Rows are drawn by the grid, so each row is a peer of its own, cached by its
/// display index. The cache is dropped when the items or the sort order change:
/// index n is then another record, and a bridge holding the old peer must learn
/// that from the structure event rather than read a different record through it.
/// </remarks>
public class DataGridPeer : UIElementPeer
{
    private readonly DataGridView _grid;
    private readonly Dictionary<int, DataGridRowPeer> _rows = [];
    private DataGridHeaderRowPeer? _header;

    public DataGridPeer(DataGridView owner) : base(owner)
    {
        _grid = owner;

        owner.Items.CollectionChanged += OnRowsChanged;
        owner.SortChanged += (_, _) => OnRowsChanged(null, null);

        owner.SelectionChanged += (_, _) =>
        {
            if (FocusedDescendant is { } current)
                AccessibilityEvents.RaiseFocusChanged(current);
        };
    }

    internal DataGridView Grid => _grid;

    protected override AccessibilityRole DefaultRole => AccessibilityRole.Table;

    public override IReadOnlyList<AccessibilityPeer> Children
    {
        get
        {
            _header ??= new DataGridHeaderRowPeer(this);

            var children = new List<AccessibilityPeer>(_grid.Items.Count + 1) { _header };

            for (int row = 0; row < _grid.Items.Count; row++)
                children.Add(RowPeer(row));

            return children;
        }
    }

    /// <summary>The selected row, while the grid has the focus.</summary>
    public override AccessibilityPeer? FocusedDescendant =>
        IsFocused(_grid) && _grid.SelectedIndex >= 0 ? RowPeer(_grid.SelectedIndex) : null;

    /// <summary>The grid takes no keyboard focus yet; it does once it is an input
    /// element, and then its current row is reported as the focus.</summary>
    internal static bool IsFocused(DataGridView grid) => grid is IInputElement { IsFocused: true };

    internal DataGridRowPeer RowPeer(int row) =>
        _rows.TryGetValue(row, out DataGridRowPeer? peer) ? peer : _rows[row] = new DataGridRowPeer(this, row);

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs? e)
    {
        _rows.Clear();
        RaiseStructureChanged();
    }
}

/// <summary>A data row: named by its cells, selected as a whole.</summary>
public class DataGridRowPeer : AccessibilityPeer
{
    private readonly DataGridPeer _grid;
    private readonly int _row;
    private List<AccessibilityPeer>? _cells;

    internal DataGridRowPeer(DataGridPeer grid, int row)
    {
        _grid = grid;
        _row = row;
    }

    private DataGridView Grid => _grid.Grid;

    public override AccessibilityRole Role => AccessibilityRole.Row;

    /// <summary>The cells read in a row — what a sighted user sees across it.</summary>
    public override string Name =>
        _row < Grid.Items.Count
            ? string.Join(", ", Grid.Columns.Select(column => column.TextOf(Grid.RowItem(_row))))
            : string.Empty;

    public override AccessibilityStates States
    {
        get
        {
            AccessibilityStates states = AccessibilityStates.Selectable;

            if (Grid.SelectedIndex == _row)
            {
                states |= AccessibilityStates.Selected;
                if (DataGridPeer.IsFocused(Grid)) states |= AccessibilityStates.Focused;
            }

            if (!Grid.IsEffectivelyEnabled) states |= AccessibilityStates.Disabled;

            return states;
        }
    }

    public override AccessibilityActions Actions =>
        AccessibilityActions.Select | AccessibilityActions.ScrollIntoView;

    /// <summary>One-based among the data rows: the header row is not counted.</summary>
    public override SetPosition? Position => new(_row + 1, Grid.Items.Count);

    public override AccessibilityPeer? Parent => _grid;

    public override IReadOnlyList<AccessibilityPeer> Children =>
        _cells ??= [.. Grid.Columns.Select((_, column) => (AccessibilityPeer)new DataGridCellPeer(this, _row, column))];

    /// <summary>Clipped to the visible body: a row scrolled out has no bounds.</summary>
    public override Rectangle Bounds => Grid.RowBounds(_row).Intersect(Grid.VisibleBodyBounds);

    public override Form? Form => _grid.Form;

    public override bool Select()
    {
        if (!Grid.IsEffectivelyEnabled || _row >= Grid.Items.Count) return false;

        Grid.SelectedIndex = _row;
        return true;
    }

    public override bool ScrollIntoView()
    {
        Grid.ScrollIntoView(_row);
        return true;
    }

    internal DataGridView GridView => Grid;
}

/// <summary>A cell: its text is both its name and its value.</summary>
public class DataGridCellPeer : AccessibilityPeer
{
    private readonly DataGridRowPeer _row;
    private readonly int _rowIndex;
    private readonly int _column;

    internal DataGridCellPeer(DataGridRowPeer row, int rowIndex, int column)
    {
        _row = row;
        _rowIndex = rowIndex;
        _column = column;
    }

    private DataGridView Grid => _row.GridView;

    public override AccessibilityRole Role => AccessibilityRole.Cell;

    public override string Name =>
        _rowIndex < Grid.Items.Count && _column < Grid.Columns.Count
            ? Grid.Columns[_column].TextOf(Grid.RowItem(_rowIndex))
            : string.Empty;

    public override string? Value => Name;

    public override AccessibilityStates States =>
        Grid.IsEffectivelyEnabled ? AccessibilityStates.ReadOnly : AccessibilityStates.ReadOnly | AccessibilityStates.Disabled;

    /// <summary>The column within the row: "column 2 of 4".</summary>
    public override SetPosition? Position => new(_column + 1, Grid.Columns.Count);

    public override AccessibilityPeer? Parent => _row;

    public override IReadOnlyList<AccessibilityPeer> Children => [];

    /// <summary>The row's band: column geometry is the grid's own, and a cell is
    /// found by a screen reader through the row anyway.</summary>
    public override Rectangle Bounds => _row.Bounds;

    public override Form? Form => _row.Form;
}

/// <summary>The header row: the column headers, in order.</summary>
public class DataGridHeaderRowPeer : AccessibilityPeer
{
    private readonly DataGridPeer _grid;
    private readonly Dictionary<DataGridViewColumn, AccessibilityPeer> _headers = [];

    internal DataGridHeaderRowPeer(DataGridPeer grid) => _grid = grid;

    public override AccessibilityRole Role => AccessibilityRole.Row;

    public override string Name => string.Empty;

    public override AccessibilityPeer? Parent => _grid;

    public override IReadOnlyList<AccessibilityPeer> Children =>
        [.. _grid.Grid.Columns.Select(column =>
            _headers.TryGetValue(column, out AccessibilityPeer? peer)
                ? peer
                : _headers[column] = new DataGridColumnHeaderPeer(this, column))];

    public override Rectangle Bounds
    {
        get
        {
            Rectangle body = _grid.Grid.VisibleBodyBounds;
            float height = _grid.Grid.HeaderHeight;

            return new Rectangle(new Point(body.X, body.Y - height), new Size(body.Width, height));
        }
    }

    public override Form? Form => _grid.Form;

    internal DataGridView Grid => _grid.Grid;
}

/// <summary>A column header: its sort order is part of its description.</summary>
public class DataGridColumnHeaderPeer : AccessibilityPeer
{
    private readonly DataGridHeaderRowPeer _row;
    private readonly DataGridViewColumn _column;

    internal DataGridColumnHeaderPeer(DataGridHeaderRowPeer row, DataGridViewColumn column)
    {
        _row = row;
        _column = column;
    }

    private DataGridView Grid => _row.Grid;

    private int Index => Grid.Columns.IndexOf(_column);

    public override AccessibilityRole Role => AccessibilityRole.ColumnHeader;

    public override string Name => _column.Header ?? string.Empty;

    /// <summary>"Sorted ascending" and the like are words of the bridge's platform;
    /// the peer only says which way, in the value.</summary>
    public override string? Value =>
        Grid.SortColumnIndex != Index ? null
        : Grid.SortDescending ? "descending" : "ascending";

    public override AccessibilityActions Actions =>
        _column.CanSort && Grid.CanSortByHeaderClick ? AccessibilityActions.Invoke : AccessibilityActions.None;

    public override SetPosition? Position => new(Index + 1, Grid.Columns.Count);

    public override AccessibilityPeer? Parent => _row;

    public override IReadOnlyList<AccessibilityPeer> Children => [];

    public override Rectangle Bounds => _row.Bounds;

    public override Form? Form => _row.Form;

    /// <summary>As a click on the header: sort by it, the second time the other way.</summary>
    public override bool Invoke()
    {
        if (!_column.CanSort || !Grid.CanSortByHeaderClick || !Grid.IsEffectivelyEnabled) return false;

        Grid.SortBy(Index);
        return true;
    }
}