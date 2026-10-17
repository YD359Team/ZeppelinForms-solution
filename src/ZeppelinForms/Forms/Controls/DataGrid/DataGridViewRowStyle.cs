using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Forms.Controls.DataGrid;

/// <summary>The look of one row of a <see cref="DataGridView"/>, given by
/// <see cref="DataGridView.RowStyle"/> for its record. A part left out keeps the
/// grid's own.</summary>
/// <example>
/// <code>
/// var hung = new DataGridViewRowStyle { Background = new Color(40, 220, 40, 40) };
/// grid.RowStyle = item => ((ProcessData)item).Responding ? null : hung;
/// </code>
/// </example>
public sealed class DataGridViewRowStyle
{
    /// <summary>The row's fill, over the stripe; transparent — none.</summary>
    public Color Background { get; init; }

    /// <summary>The text of the row's cells; transparent — the grid's text color.</summary>
    public Color TextColor { get; init; }

    /// <summary>The font of the row's cells; null — the grid's font.</summary>
    public Font? Font { get; init; }
}