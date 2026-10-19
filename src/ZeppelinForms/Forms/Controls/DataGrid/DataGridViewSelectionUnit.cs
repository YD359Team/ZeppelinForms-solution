namespace ZeppelinForms.Forms.Controls.DataGrid;

/// <summary>What a click selects in a grid: a whole row, a single cell or a whole column.</summary>
/// <remarks>How many of them — one, a toggled set, ranges with Shift — is
/// <see cref="DataGridView.SelectionMode"/>, the same modes a list box has.</remarks>
public enum DataGridViewSelectionUnit
{
    /// <summary>A row across all its columns: a list of records.</summary>
    FullRow,

    /// <summary>A cell: a sheet of values, as a spreadsheet selects.</summary>
    Cell,

    /// <summary>A column down all its rows. A click on its header selects it
    /// rather than sorting by it: <see cref="DataGridView.SortBy"/> still sorts.</summary>
    FullColumn,
}
