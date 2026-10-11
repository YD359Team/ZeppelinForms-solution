using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.Forms.Controls.DataGrid;

/// <summary>The grid from the keyboard: it takes the focus, and the arrows,
/// PageUp, PageDown, Home and End move the selected row, scrolling it into view.</summary>
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

        int current = SelectedIndex;

        // a page is what fits in the body, less one row kept for context —
        // the row that was at the edge stays in sight after the jump
        int page = Math.Max(1, (int)(BodyBounds.Height / RowHeight) - 1);

        int? next = e.Key switch
        {
            Key.Down => current < 0 ? 0 : current + 1,
            Key.Up => current < 0 ? 0 : current - 1,
            Key.PageDown => Math.Max(current, 0) + page,
            Key.PageUp => current - page,
            Key.Home => 0,
            Key.End => Items.Count - 1,
            _ => null,
        };

        if (next is not { } row)
        {
            base.OnKeyDown(e);
            return;
        }

        row = Math.Clamp(row, 0, Items.Count - 1);

        SelectedIndex = row;
        ScrollIntoView(row);

        e.Handled = true;
    }
}