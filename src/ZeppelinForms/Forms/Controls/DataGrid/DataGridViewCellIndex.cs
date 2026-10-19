namespace ZeppelinForms.Forms.Controls.DataGrid;

/// <summary>A cell of a grid: its row on screen and its column in
/// <see cref="DataGridView.Columns"/>.</summary>
/// <remarks>The row is the place on screen, as <see cref="DataGridView.SelectedIndex"/>
/// is: with sorting it is not the index in <see cref="DataGridView.Items"/> —
/// <see cref="DataGridView.RowItem(int)"/> gives the record.</remarks>
public readonly record struct DataGridViewCellIndex(int Row, int Column);
