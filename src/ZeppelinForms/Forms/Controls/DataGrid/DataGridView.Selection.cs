using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using System.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Markup;

namespace ZeppelinForms.Forms.Controls.DataGrid;

/// <summary>Selection: rows, cells or columns — one, a toggled set, or ranges.</summary>
/// <remarks>
/// <para>
/// What is selected is told by <see cref="SelectionUnit"/> — a row, a cell, a column —
/// and how many by <see cref="SelectionMode"/>, the modes a list box has: one at a
/// time, toggled by clicks, or ranges with Shift and single toggles with Ctrl.
/// </para>
/// <para>
/// The selection is kept by record, not by row index, as the single selection was
/// before: a sort, a row inserted above or a refresh keep the same records selected,
/// on whatever rows they are now. A record removed from <see cref="Items"/> leaves the
/// selection. A record that is in Items twice is selected on both of its rows.
/// </para>
/// <para>
/// The lead — <see cref="SelectedIndex"/> and <see cref="CurrentColumnIndex"/> — is
/// where the keyboard is: the row and the cell the arrows move from and the outline
/// is drawn around. With a single selection it is the selection itself.
/// </para>
/// </remarks>
public partial class DataGridView
{
    /// <summary>A selected cell: its record and its column, both by reference — a
    /// record type's Equals must not select another record's cell.</summary>
    private readonly struct CellKey(object record, DataGridViewColumn column) : IEquatable<CellKey>
    {
        public object Record { get; } = record;

        public DataGridViewColumn Column { get; } = column;

        public bool Equals(CellKey other) =>
            ReferenceEquals(Record, other.Record) && ReferenceEquals(Column, other.Column);

        public override bool Equals(object? obj) => obj is CellKey other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(RuntimeHelpers.GetHashCode(Record), RuntimeHelpers.GetHashCode(Column));
    }

    /// <summary>How a press changes the selection.</summary>
    private enum SelectGesture
    {
        /// <summary>This one alone.</summary>
        Only,

        /// <summary>This one in or out, the rest as they are.</summary>
        Toggle,

        /// <summary>From the anchor to this one, in place of the rest.</summary>
        Range,
    }

    private readonly HashSet<object> _selectedRows = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<CellKey> _selectedCells = [];
    private readonly HashSet<DataGridViewColumn> _selectedColumns = new(ReferenceEqualityComparer.Instance);

    /// <summary>Where a Shift range starts: the record and the column of the last
    /// press without Shift.</summary>
    private object? _anchorRecord;
    private int _anchorColumn = -1;

    /// <summary>The column of the lead cell; −1 — none yet.</summary>
    private int _currentColumn = -1;

    /// <summary>One at a time, toggled by clicks, or ranges with Shift and toggles
    /// with Ctrl — as in a list box. Single by default.</summary>
    /// <remarks>Turned to Single, the selection keeps only the lead.</remarks>
    public SelectionMode SelectionMode
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            if (value == SelectionMode.Single) SelectLeadOnly();
        }
    }

    /// <summary>What a click selects: a row, a cell or a column. A row by default.</summary>
    /// <remarks>Changed, the selection starts over from the lead: the row selected
    /// becomes the cell selected in the current column, and so on.</remarks>
    public DataGridViewSelectionUnit SelectionUnit
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            SelectLeadOnly();
        }
    }

    /// <summary>Put the column headers on the first line of what
    /// <see cref="CopySelection"/> copies.</summary>
    public bool CopyHeaders { get; set; }

    /// <summary>The lead row: with a single selection — the selected row; with several —
    /// the one the keyboard is on, selected or clicked last. −1 — none.</summary>
    /// <remarks>Set, it selects this row alone — or, selecting cells, its cell in the
    /// current column. Selecting columns, it only moves the lead.</remarks>
    [SetAfterContent]
    public int SelectedIndex
    {
        get => _leadRow;
        set
        {
            int clamped = value < 0 || value >= Items.Count ? -1 : value;

            // the record is remembered on every assignment, even a repeated one:
            // it is what the selection is restored by after the data changes
            _selectedRecord = clamped >= 0 ? RowItem(clamped) : null;

            if (SelectionUnit == DataGridViewSelectionUnit.FullColumn)
            {
                MoveLead(clamped, _currentColumn, raise: true);
                return;
            }

            if (clamped < 0)
            {
                ClearSelection();
                return;
            }

            SelectAt(clamped, LeadColumnOrFirst, SelectGesture.Only);
        }
    }

    private int _leadRow = -1;

    public object? SelectedItem => SelectedIndex >= 0 ? RowItem(SelectedIndex) : null;

    /// <summary>The column of the lead cell, where the keyboard is when selecting cells
    /// or columns. −1 — none.</summary>
    /// <remarks>Set, it selects this cell of the lead row alone, or this column alone.
    /// Selecting rows it only moves the lead.</remarks>
    public int CurrentColumnIndex
    {
        get => _currentColumn >= 0 && _currentColumn < Columns.Count ? _currentColumn : -1;
        set
        {
            int clamped = value < 0 || value >= Columns.Count ? -1 : value;

            if (SelectionUnit == DataGridViewSelectionUnit.FullRow || clamped < 0 ||
                (SelectionUnit == DataGridViewSelectionUnit.Cell && SelectedIndex < 0))
            {
                MoveLead(SelectedIndex, clamped, raise: true);
                return;
            }

            SelectAt(SelectedIndex, clamped, SelectGesture.Only);
        }
    }

    /// <summary>The lead's column, or the first one when there is none yet: a row
    /// selected as a cell needs a column.</summary>
    private int LeadColumnOrFirst => CurrentColumnIndex >= 0 ? CurrentColumnIndex : Columns.Count > 0 ? 0 : -1;

    /// <summary>The data of a row by its place on screen, as the type it is. With
    /// sorting this is not the same as Items[row].</summary>
    /// <exception cref="InvalidCastException">The record is not a <typeparamref name="T"/>.</exception>
    public T RowItem<T>(int row) => (T)RowItem(row);

    /// <summary>The value of a cell, as its column takes it out of the record.</summary>
    public object? CellValue(int row, int column) => Columns[column].Value(RowItem(row));

    /// <summary>The text of a cell, as it is drawn.</summary>
    public string CellText(int row, int column) => Columns[column].TextOf(RowItem(row));

    // ===== what is selected =====

    /// <summary>Whether a row is selected as a whole — selecting rows.</summary>
    public bool IsRowSelected(int row) =>
        SelectionUnit == DataGridViewSelectionUnit.FullRow &&
        row >= 0 && row < Items.Count &&
        _selectedRows.Contains(RowItem(row));

    /// <summary>Whether a column is selected as a whole — selecting columns.</summary>
    public bool IsColumnSelected(int column) =>
        SelectionUnit == DataGridViewSelectionUnit.FullColumn &&
        column >= 0 && column < Columns.Count &&
        _selectedColumns.Contains(Columns[column]);

    /// <summary>Whether a cell is in the selection: selected itself, or in a selected
    /// row or column.</summary>
    public bool IsCellSelected(int row, int column)
    {
        if (row < 0 || row >= Items.Count || column < 0 || column >= Columns.Count) return false;

        return SelectionUnit switch
        {
            DataGridViewSelectionUnit.FullRow => _selectedRows.Contains(RowItem(row)),
            DataGridViewSelectionUnit.FullColumn => _selectedColumns.Contains(Columns[column]),
            _ => _selectedCells.Contains(new CellKey(RowItem(row), Columns[column])),
        };
    }

    /// <summary>The rows with something selected in them, on screen, from the top:
    /// the selected rows, or the rows of the selected cells. Selecting columns — none.</summary>
    /// <remarks>Walks the rows: ask once, not per row — <see cref="IsRowSelected"/> is for that.</remarks>
    public IReadOnlyList<int> SelectedIndices
    {
        get
        {
            List<int> rows = [];

            if (SelectionUnit == DataGridViewSelectionUnit.FullColumn) return rows;

            HashSet<object> records = SelectionUnit == DataGridViewSelectionUnit.FullRow
                ? _selectedRows
                : new HashSet<object>(_selectedCells.Select(cell => cell.Record), ReferenceEqualityComparer.Instance);

            if (records.Count == 0) return rows;

            for (int row = 0; row < Items.Count; row++)
                if (records.Contains(RowItem(row)))
                    rows.Add(row);

            return rows;
        }
    }

    /// <summary>The records of <see cref="SelectedIndices"/>, in the same order.</summary>
    public IReadOnlyList<object> SelectedItems => [.. SelectedIndices.Select(RowItem)];

    /// <summary>The selected columns, in the order of <see cref="Columns"/> — selecting columns.</summary>
    public IReadOnlyList<DataGridViewColumn> SelectedColumns =>
        SelectionUnit == DataGridViewSelectionUnit.FullColumn
            ? [.. Columns.Where(_selectedColumns.Contains)]
            : [];

    /// <summary>Every cell in the selection, row by row from the top, left to right:
    /// the selected cells, or every cell of the selected rows or columns.</summary>
    /// <remarks>A selected column is a cell per row: in a long table ask
    /// <see cref="SelectedColumns"/> instead.</remarks>
    public IReadOnlyList<DataGridViewCellIndex> SelectedCells
    {
        get
        {
            List<DataGridViewCellIndex> cells = [];

            if (!HasSelection) return cells;

            for (int row = 0; row < Items.Count; row++)
                for (int column = 0; column < Columns.Count; column++)
                    if (IsCellSelected(row, column))
                        cells.Add(new DataGridViewCellIndex(row, column));

            return cells;
        }
    }

    private bool HasSelection => SelectionUnit switch
    {
        DataGridViewSelectionUnit.FullRow => _selectedRows.Count > 0,
        DataGridViewSelectionUnit.FullColumn => _selectedColumns.Count > 0,
        _ => _selectedCells.Count > 0,
    };

    public event EventHandler<object?>? SelectionChanged;

    // ===== changing it from code =====

    /// <summary>Select a row or take it out of the selection — selecting rows.
    /// With a single selection a row selected replaces the one there was.</summary>
    /// <exception cref="InvalidOperationException">The grid selects cells or columns.</exception>
    public void SetRowSelected(int row, bool selected)
    {
        RequireUnit(DataGridViewSelectionUnit.FullRow);
        SetSelected(row, CurrentColumnIndex, selected);
    }

    /// <summary>Select a cell or take it out of the selection — selecting cells.</summary>
    /// <exception cref="InvalidOperationException">The grid selects rows or columns.</exception>
    public void SetCellSelected(int row, int column, bool selected)
    {
        RequireUnit(DataGridViewSelectionUnit.Cell);
        SetSelected(row, column, selected);
    }

    /// <summary>Select a column or take it out of the selection — selecting columns.</summary>
    /// <exception cref="InvalidOperationException">The grid selects rows or cells.</exception>
    public void SetColumnSelected(int column, bool selected)
    {
        RequireUnit(DataGridViewSelectionUnit.FullColumn);
        SetSelected(SelectedIndex, column, selected);
    }

    /// <summary>Select every row, cell or column. Nothing with a single selection.</summary>
    /// <remarks>Selecting cells it is one entry per cell: in a long table selecting
    /// rows or columns is the lighter way to select everything.</remarks>
    public void SelectAll()
    {
        if (SelectionMode == SelectionMode.Single) return;

        switch (SelectionUnit)
        {
            case DataGridViewSelectionUnit.FullRow:
                foreach (object record in Items) _selectedRows.Add(record);
                break;

            case DataGridViewSelectionUnit.FullColumn:
                foreach (DataGridViewColumn column in Columns) _selectedColumns.Add(column);
                break;

            default:
                foreach (object record in Items)
                    foreach (DataGridViewColumn column in Columns)
                        _selectedCells.Add(new CellKey(record, column));
                break;
        }

        RaiseSelectionChanged();
    }

    /// <summary>Select nothing. The lead stays where it is for the keyboard, unless
    /// selecting rows — there the lead row is the selection.</summary>
    public void ClearSelection()
    {
        bool had = ClearSets();

        bool leadMoved = false;

        if (SelectionUnit != DataGridViewSelectionUnit.FullColumn && SelectedIndex >= 0)
        {
            SetLeadRow(-1);
            leadMoved = true;
        }

        _anchorRecord = null;
        _anchorColumn = -1;

        if (had || leadMoved) RaiseSelectionChanged();
    }

    /// <summary>Copy the selection to the clipboard as text: a line per row, its
    /// cells apart by tabs — what a spreadsheet pastes as a table.</summary>
    public void CopySelection()
    {
        string text = GetSelectionText();

        if (text.Length > 0) Clipboard.Current.SetText(text);
    }

    /// <summary>The selection as <see cref="CopySelection"/> copies it: the rows with
    /// something selected, the columns with something selected, and empty places for
    /// the cells in between that are not.</summary>
    public string GetSelectionText()
    {
        if (!HasSelection || Columns.Count == 0) return string.Empty;

        List<int> rows = SelectionUnit == DataGridViewSelectionUnit.FullColumn
            ? [.. Enumerable.Range(0, Items.Count)]
            : [.. SelectedIndices];

        List<int> columns = SelectionUnit switch
        {
            DataGridViewSelectionUnit.FullRow => [.. Enumerable.Range(0, Columns.Count)],
            DataGridViewSelectionUnit.FullColumn => [.. Enumerable.Range(0, Columns.Count).Where(IsColumnSelected)],
            _ => [.. Enumerable.Range(0, Columns.Count).Where(column => _selectedCells.Any(cell => ReferenceEquals(cell.Column, Columns[column])))],
        };

        var text = new StringBuilder();

        if (CopyHeaders)
            AppendLine(text, columns.Select(column => Columns[column].Header ?? string.Empty));

        foreach (int row in rows)
            AppendLine(text, columns.Select(column => IsCellSelected(row, column) ? CellText(row, column) : string.Empty));

        return text.ToString();

        static void AppendLine(StringBuilder text, IEnumerable<string> cells)
        {
            if (text.Length > 0) text.Append(Environment.NewLine);

            // a tab or a line break inside a value would cut the table apart
            text.AppendJoin('\t', cells.Select(cell => cell.ReplaceLineEndings(" ").Replace('\t', ' ')));
        }
    }

    private void RequireUnit(DataGridViewSelectionUnit unit)
    {
        if (SelectionUnit != unit)
        {
            throw new InvalidOperationException(
                $"The grid selects by {SelectionUnit}, not by {unit}: set SelectionUnit first.");
        }
    }

    private void SetSelected(int row, int column, bool selected)
    {
        if (selected)
        {
            SelectAt(row, column, SelectionMode == SelectionMode.Single ? SelectGesture.Only : SelectGesture.Toggle, addOnly: true);
            return;
        }

        if (!RemoveUnit(row, column)) return;

        // with nothing left of a single selection there is no lead row either
        if (SelectionMode == SelectionMode.Single && SelectionUnit != DataGridViewSelectionUnit.FullColumn)
            SetLeadRow(-1);

        RaiseSelectionChanged();
    }

    // ===== the machinery =====

    private bool IsRecordSelected(object record) =>
        _selectedRows.Contains(record) || _selectedCells.Any(cell => ReferenceEquals(cell.Record, record));

    /// <summary>A press — of the mouse, of a key, or from code — on a row and a column:
    /// the selection changes by the gesture, and the lead moves there.</summary>
    /// <param name="addOnly">Toggle only in: a cell selected from code stays selected.</param>
    private void SelectAt(int row, int column, SelectGesture gesture, bool addOnly = false)
    {
        if (SelectionMode == SelectionMode.Single) gesture = SelectGesture.Only;

        bool changed;

        switch (gesture)
        {
            case SelectGesture.Range when RangeFrom() is { } anchor:
                changed = ClearSets();
                changed |= AddRange(anchor.Row, anchor.Column, row, column);
                break;

            case SelectGesture.Toggle:
                changed = addOnly || !IsUnitSelected(row, column)
                    ? AddUnit(row, column)
                    : RemoveUnit(row, column);

                SetAnchor(row, column);
                break;

            default:
                // the same place selected again is no change: SelectedIndex set to the
                // row it already is raised no SelectionChanged before 0.17, and doesn't now
                if (SelectionCount == 1 && IsUnitSelected(row, column))
                {
                    changed = false;
                }
                else
                {
                    changed = ClearSets();
                    changed |= AddUnit(row, column);
                }

                SetAnchor(row, column);
                break;
        }

        changed |= MoveLead(row, column, raise: false);

        if (changed) RaiseSelectionChanged();
    }

    /// <summary>Make the lead the whole selection: after the mode or the unit changed.</summary>
    private void SelectLeadOnly()
    {
        int row = SelectedIndex;
        int column = CurrentColumnIndex;

        bool hasLead = SelectionUnit switch
        {
            DataGridViewSelectionUnit.FullRow => row >= 0,
            DataGridViewSelectionUnit.FullColumn => column >= 0,
            _ => row >= 0,
        };

        if (!hasLead)
        {
            if (ClearSets()) RaiseSelectionChanged();
            return;
        }

        SelectAt(row, SelectionUnit == DataGridViewSelectionUnit.Cell ? LeadColumnOrFirst : column, SelectGesture.Only);
    }

    private void SetAnchor(int row, int column)
    {
        _anchorRecord = row >= 0 && row < Items.Count ? RowItem(row) : null;
        _anchorColumn = column;
    }

    /// <summary>Where a Shift range starts; null — there is no anchor, and the press
    /// selects the one place alone.</summary>
    private (int Row, int Column)? RangeFrom()
    {
        if (SelectionUnit == DataGridViewSelectionUnit.FullColumn)
            return _anchorColumn >= 0 && _anchorColumn < Columns.Count ? (SelectedIndex, _anchorColumn) : null;

        int row = DisplayIndexOf(_anchorRecord);

        return row >= 0 ? (row, _anchorColumn) : null;
    }

    private bool IsUnitSelected(int row, int column) => SelectionUnit switch
    {
        DataGridViewSelectionUnit.FullRow => IsRowSelected(row),
        DataGridViewSelectionUnit.FullColumn => IsColumnSelected(column),
        _ => IsCellSelected(row, column),
    };

    private bool AddUnit(int row, int column)
    {
        bool hasRow = row >= 0 && row < Items.Count;
        bool hasColumn = column >= 0 && column < Columns.Count;

        return SelectionUnit switch
        {
            DataGridViewSelectionUnit.FullRow => hasRow && _selectedRows.Add(RowItem(row)),
            DataGridViewSelectionUnit.FullColumn => hasColumn && _selectedColumns.Add(Columns[column]),
            _ => hasRow && hasColumn && _selectedCells.Add(new CellKey(RowItem(row), Columns[column])),
        };
    }

    private bool RemoveUnit(int row, int column)
    {
        bool hasRow = row >= 0 && row < Items.Count;
        bool hasColumn = column >= 0 && column < Columns.Count;

        return SelectionUnit switch
        {
            DataGridViewSelectionUnit.FullRow => hasRow && _selectedRows.Remove(RowItem(row)),
            DataGridViewSelectionUnit.FullColumn => hasColumn && _selectedColumns.Remove(Columns[column]),
            _ => hasRow && hasColumn && _selectedCells.Remove(new CellKey(RowItem(row), Columns[column])),
        };
    }

    /// <summary>Everything between two places: rows, a block of cells, columns.</summary>
    private bool AddRange(int fromRow, int fromColumn, int toRow, int toColumn)
    {
        bool changed = false;

        switch (SelectionUnit)
        {
            case DataGridViewSelectionUnit.FullRow:
                for (int row = Math.Min(fromRow, toRow); row <= Math.Max(fromRow, toRow); row++)
                    changed |= AddUnit(row, -1);
                break;

            case DataGridViewSelectionUnit.FullColumn:
                for (int column = Math.Min(fromColumn, toColumn); column <= Math.Max(fromColumn, toColumn); column++)
                    changed |= AddUnit(-1, column);
                break;

            default:
                for (int row = Math.Min(fromRow, toRow); row <= Math.Max(fromRow, toRow); row++)
                    for (int column = Math.Min(fromColumn, toColumn); column <= Math.Max(fromColumn, toColumn); column++)
                        changed |= AddUnit(row, column);
                break;
        }

        return changed;
    }

    private int SelectionCount => _selectedRows.Count + _selectedCells.Count + _selectedColumns.Count;

    private bool ClearSets()
    {
        bool had = _selectedRows.Count > 0 || _selectedCells.Count > 0 || _selectedColumns.Count > 0;

        _selectedRows.Clear();
        _selectedCells.Clear();
        _selectedColumns.Clear();

        return had;
    }

    /// <summary>Move the lead to a row and a column; true — it moved.</summary>
    private bool MoveLead(int row, int column, bool raise)
    {
        int before = SelectedIndex;
        int beforeColumn = _currentColumn;

        SetLeadRow(row >= 0 && row < Items.Count ? row : -1);

        if (column >= 0 && column < Columns.Count) _currentColumn = column;

        bool moved = before != SelectedIndex || beforeColumn != _currentColumn;

        if (moved && raise) RaiseSelectionChanged();
        else if (moved) InvalidateVisual();

        return moved;
    }

    /// <summary>The lead row and its record, nothing else.</summary>
    private void SetLeadRow(int row)
    {
        // not through SelectedIndex: its setter selects, and this only moves
        _leadRow = row;
        _selectedRecord = row >= 0 ? RowItem(row) : null;
    }

    private void RaiseSelectionChanged()
    {
        SelectionChanged?.Invoke(this, SelectedItem);
        InvalidateVisual();
    }

    /// <summary>The place on screen of a record; −1 — it is not in the rows.</summary>
    private int DisplayIndexOf(object? record)
    {
        if (record is null) return -1;

        for (int row = 0; row < Items.Count; row++)
            if (ReferenceEquals(RowItem(row), record))
                return row;

        return -1;
    }

    /// <summary>The rows changed or were sorted: the records that left go out of the
    /// selection, and the lead follows its record to the row it is on now.</summary>
    /// <remarks>
    /// The order and the selection are rebuilt from the record, not from the old
    /// index. Previously ApplySort read SelectedItem here — through the old order,
    /// whose indices no longer matched Items — and removing a row from a sorted
    /// table could throw ArgumentOutOfRangeException. Without sorting the selection
    /// quietly moved to a neighbouring record.
    /// </remarks>
    private void RestoreSelection(NotifyCollectionChangedEventArgs? change)
    {
        bool changed = change is not null && ForgetRemovedRecords(change);

        int before = SelectedIndex;
        int row = DisplayIndexOf(_selectedRecord);

        SetLeadRow(row);

        if (DisplayIndexOf(_anchorRecord) < 0) _anchorRecord = null;

        if (changed || before != row) RaiseSelectionChanged();
    }

    /// <summary>Take the records no longer in Items out of the selection. Only the
    /// records the change took away are looked for: adding rows one by one to a grid
    /// with a selection walks no rows.</summary>
    private bool ForgetRemovedRecords(NotifyCollectionChangedEventArgs change)
    {
        if (_selectedRows.Count == 0 && _selectedCells.Count == 0) return false;

        List<object> candidates = change.Action == NotifyCollectionChangedAction.Reset
            ? [.. _selectedRows, .. _selectedCells.Select(cell => cell.Record)]
            : [.. (change.OldItems?.Cast<object>() ?? []).Where(IsRecordSelected)];

        if (candidates.Count == 0) return false;

        var present = new HashSet<object>(Items, ReferenceEqualityComparer.Instance);
        bool changed = false;

        foreach (object record in candidates)
        {
            if (present.Contains(record)) continue;

            changed |= _selectedRows.Remove(record);
            changed |= _selectedCells.RemoveWhere(cell => ReferenceEquals(cell.Record, record)) > 0;
        }

        return changed;
    }
}
