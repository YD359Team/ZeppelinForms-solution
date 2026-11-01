using System.Collections.Specialized;

namespace ZeppelinForms.Forms.Controls.DataGrid;

/// <summary>The columns changing under a grid that is shown: what was kept by a column's
/// index — the sort, the current cell, the anchor of a range — follows its column to its
/// new place, and what was of a column taken out goes with it.</summary>
public partial class DataGridView
{
    /// <summary>The columns as they were before the last change: an index kept from then
    /// is read through it to the column it meant.</summary>
    private DataGridViewColumn[] _columnsBefore = [];

    private void OnColumnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        DataGridViewColumn[] before = _columnsBefore;
        _columnsBefore = [.. Columns];

        _hoveredHeader = -1;
        _resizingColumn = -1;

        // the sort keeps to its column; its column gone, the rows go back to the source's order
        if (SortColumnIndex >= 0)
        {
            int sorted = Follow(before, SortColumnIndex);

            if (sorted >= 0) SortColumnIndex = sorted;
            else ClearSort();
        }

        _currentColumn = Follow(before, _currentColumn);
        _anchorColumn = Follow(before, _anchorColumn);

        // the cells and columns selected in a column taken out are no longer selected
        var present = new HashSet<DataGridViewColumn>(Columns, ReferenceEqualityComparer.Instance);
        int dropped = _selectedColumns.RemoveWhere(column => !present.Contains(column)) +
                      _selectedCells.RemoveWhere(cell => !present.Contains(cell.Column));

        // the column being edited taken out: there is nowhere to write
        if (_editColumn is not null && !present.Contains(_editColumn)) CancelEdit();

        if (dropped > 0) RaiseSelectionChanged();

        // the widths are of the old columns: the next layout measures the new ones
        Invalidate();
    }

    /// <summary>Where the column at an index of the old columns stands now; −1 — it was
    /// taken out, or there was none.</summary>
    private int Follow(DataGridViewColumn[] before, int index) =>
        index >= 0 && index < before.Length ? Columns.IndexOf(before[index]) : -1;
}
