namespace ZeppelinForms.Forms.Controls.DataGrid;

/// <summary>A cell, by its record and its column: the row of a record changes with
/// sorting, and the record stays.</summary>
public class DataGridViewCellEventArgs(object item, DataGridViewColumn column) : EventArgs
{
    /// <summary>The record of the row.</summary>
    public object Item { get; } = item;

    public DataGridViewColumn Column { get; } = column;
}

/// <summary>Editing a cell is about to begin; set <see cref="Cancel"/> to keep it from it.</summary>
public class DataGridViewCellEditingEventArgs(object item, DataGridViewColumn column)
    : DataGridViewCellEventArgs(item, column)
{
    public bool Cancel { get; set; }
}

/// <summary>A value is about to be written into a cell: it can be changed, or the
/// writing cancelled — the cell keeps its value.</summary>
public class DataGridViewCellValueChangingEventArgs(object item, DataGridViewColumn column, object? oldValue, object? newValue)
    : DataGridViewCellEventArgs(item, column)
{
    /// <summary>The value the cell has now.</summary>
    public object? OldValue { get; } = oldValue;

    /// <summary>The value to write; a handler may put another one.</summary>
    public object? NewValue { get; set; } = newValue;

    public bool Cancel { get; set; }
}
