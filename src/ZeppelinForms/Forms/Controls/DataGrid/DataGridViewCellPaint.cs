using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Forms.Controls.DataGrid;

/// <summary>How a cell is to be drawn: what its row looks like, and the grid for the
/// colors of its own — a check box's, the selection's.</summary>
/// <remarks>Made by the grid for every visible cell of every frame: a struct, so
/// drawing a frame doesn't make garbage.</remarks>
public readonly struct DataGridViewCellPaint
{
    internal DataGridViewCellPaint(DataGridView grid, Color textColor, Font font, bool isSelected)
    {
        Grid = grid;
        TextColor = textColor;
        Font = font;
        IsSelected = isSelected;
    }

    /// <summary>The grid the cell is in.</summary>
    public DataGridView Grid { get; }

    /// <summary>The text color of the cell: the row's style, or the selection's.</summary>
    public Color TextColor { get; }

    /// <summary>The font of the row: the grid's, or the row style's.</summary>
    public Font Font { get; }

    /// <summary>The cell is in the selection; its fill is drawn already.</summary>
    public bool IsSelected { get; }
}
