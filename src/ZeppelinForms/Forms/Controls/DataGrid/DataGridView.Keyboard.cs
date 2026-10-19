using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.Forms.Controls.DataGrid;

/// <summary>The grid from the keyboard: it takes the focus, and the arrows,
/// PageUp, PageDown, Home and End move the selected row, scrolling it into view.
/// Selecting cells or columns, they move between columns too; Shift extends,
/// Ctrl+A selects all and Ctrl+C copies. F2 edits the current cell.</summary>
/// <remarks>Before 0.13 the grid took no focus at all: a keyboard user could reach
/// every button around a table, and not a single row of it.</remarks>
public partial class DataGridView : IInputElement
{
    public bool IsFocused { get; set; }
    public bool TabStop { get; set; } = true;
    public uint TabIndex { get; set; }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Items.Count == 0 || RowHeight <= 0)
        {
            base.OnKeyDown(e);
            return;
        }

        bool control = e.Modifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.Modifiers.HasFlag(KeyModifiers.Shift);

        if (HandleEditKey(e.Key, e.Modifiers))
        {
            e.Handled = true;
            return;
        }

        if (HandleSelectionKey(e.Key, control))
        {
            e.Handled = true;
            return;
        }

        int current = SelectedIndex;
        int column = CurrentColumnIndex;

        // selecting cells or columns, the keys move between columns too: Left and
        // Right, Home and End along the row, Ctrl+Home and Ctrl+End to the first
        // and the last row — as a spreadsheet does
        bool byCell = SelectionUnit != DataGridViewSelectionUnit.FullRow && Columns.Count > 0;
        int across = Math.Max(current, 0);
        int lastColumn = Columns.Count - 1;

        // a page is what fits in the body, less one row kept for context —
        // the row that was at the edge stays in sight after the jump
        int page = Math.Max(1, (int)(BodyBounds.Height / RowHeight) - 1);

        (int Row, int Column)? next = e.Key switch
        {
            Key.Down => (current < 0 ? 0 : current + 1, column),
            Key.Up => (current < 0 ? 0 : current - 1, column),
            Key.PageDown => (Math.Max(current, 0) + page, column),
            Key.PageUp => (current - page, column),
            Key.Home when byCell && !control => (across, 0),
            Key.End when byCell && !control => (across, lastColumn),
            Key.Home => (0, column),
            Key.End => (Items.Count - 1, column),
            Key.Left when byCell => (across, Math.Max(column, 0) - 1),
            Key.Right when byCell => (across, column < 0 ? 0 : column + 1),
            _ => null,
        };

        if (next is not { } target)
        {
            base.OnKeyDown(e);
            return;
        }

        int row = Math.Clamp(target.Row, 0, Items.Count - 1);
        int col = byCell ? Math.Clamp(target.Column, 0, lastColumn) : target.Column;

        if (SelectionUnit == DataGridViewSelectionUnit.FullColumn && col == column)
        {
            // up and down a selected column the selection stays: only the lead moves
            MoveLead(row, col, raise: true);
        }
        else
        {
            // Shift with the keys extends the range from the anchor, as in any file
            // manager; without it the keys select the place they reach alone
            SelectAt(row, col, SelectionMode == SelectionMode.Extended && shift ? SelectGesture.Range : SelectGesture.Only);
        }

        ScrollIntoView(row, byCell ? col : -1);

        e.Handled = true;
    }

    /// <summary>F2 edits the current cell; Space toggles it when it is a check box —
    /// before Space toggles the selection.</summary>
    private bool HandleEditKey(Key key, KeyModifiers modifiers)
    {
        if (modifiers != KeyModifiers.None) return false;

        int row = SelectedIndex;
        int column = CurrentColumnIndex;

        return key switch
        {
            Key.F2 => BeginEdit(),
            Key.Space => row >= 0 && column >= 0 && ActivateCell(row, column),
            _ => false,
        };
    }

    /// <summary>The keys that act on the selection rather than move: Ctrl+A selects
    /// everything, Ctrl+C copies it, Space toggles the lead where clicks toggle —
    /// Ctrl+Space with Extended, as in Explorer.</summary>
    private bool HandleSelectionKey(Key key, bool control)
    {
        switch (key)
        {
            case Key.A when control && SelectionMode != SelectionMode.Single:
                SelectAll();
                return true;

            case Key.C when control:
                CopySelection();
                return true;

            case Key.Space when SelectionMode == SelectionMode.Multiple ||
                                (SelectionMode == SelectionMode.Extended && control):
                int row = Math.Max(SelectedIndex, 0);
                int column = SelectionUnit == DataGridViewSelectionUnit.FullRow ? CurrentColumnIndex : LeadColumnOrFirst;

                SelectAt(row, column, SelectGesture.Toggle);
                return true;

            default:
                return false;
        }
    }
}
