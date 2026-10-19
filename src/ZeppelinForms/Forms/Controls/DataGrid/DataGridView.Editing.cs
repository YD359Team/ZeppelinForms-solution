using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.Forms.Controls.DataGrid;

/// <summary>Editing cells: in a control put over the cell, or in place — a check box.</summary>
/// <remarks>
/// <para>
/// The cells are drawn, not controls (see the class remarks), so the control a cell is
/// edited in exists only while it is edited: the column makes it
/// (<see cref="DataGridViewColumn.CreateEditor"/>), the grid puts it over the cell as an
/// overlay of the form, and takes it away when the edit ends. A text box for a plain
/// column, a date picker, a drop-down, a numeric field for the other kinds.
/// </para>
/// <para>
/// F2 and a double click begin editing the current cell; typing on a text column begins
/// it with what is typed, as a spreadsheet does. Enter commits and goes down a row, Tab
/// commits and goes on to the next editable cell, Escape cancels, and a click elsewhere
/// commits. A value that isn't one — a number that doesn't parse — keeps the editor open.
/// </para>
/// <para>
/// A column is editable when it has <see cref="DataGridViewColumn.SetValue"/> and isn't
/// <see cref="DataGridViewColumn.ReadOnly"/>, and the grid isn't <see cref="ReadOnly"/>.
/// Every value written — from an editor or a check box clicked — goes through
/// <see cref="CellValueChanging"/>, which may change or refuse it, and
/// <see cref="CellValueChanged"/>.
/// </para>
/// </remarks>
public partial class DataGridView
{
    private UIElement? _editor;
    private object? _editRecord;
    private DataGridViewColumn? _editColumn;

    /// <summary>No cell is edited, whatever its column allows.</summary>
    public bool ReadOnly
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            if (value) CancelEdit();
        }
    }

    /// <summary>A cell is being edited in a control.</summary>
    public bool IsEditing => _editor is not null;

    /// <summary>The control the cell is being edited in; null — none is.</summary>
    public UIElement? EditingControl => _editor;

    /// <summary>The cell being edited, on screen now; null — none is.</summary>
    public DataGridViewCellIndex? EditingCell
    {
        get
        {
            if (_editRecord is null || _editColumn is null) return null;

            int row = DisplayIndexOf(_editRecord);
            int column = Columns.IndexOf(_editColumn);

            return row >= 0 && column >= 0 ? new DataGridViewCellIndex(row, column) : null;
        }
    }

    /// <summary>Editing a cell is about to begin; it can be cancelled.</summary>
    public event EventHandler<DataGridViewCellEditingEventArgs>? CellEditing;

    /// <summary>A value is about to be written into a cell; it can be changed or refused.</summary>
    public event EventHandler<DataGridViewCellValueChangingEventArgs>? CellValueChanging;

    /// <summary>A value was written into a cell.</summary>
    public event EventHandler<DataGridViewCellEventArgs>? CellValueChanged;

    /// <summary>Whether a cell can be edited: the grid and the column allow it.</summary>
    public bool IsCellEditable(int row, int column) =>
        !ReadOnly && row >= 0 && row < Items.Count && column >= 0 && column < Columns.Count &&
        Columns[column].IsEditable;

    /// <summary>Begin editing the current cell — the lead row, the current column.</summary>
    /// <returns>Whether an editor opened.</returns>
    public bool BeginEdit() => BeginEdit(SelectedIndex, CurrentColumnIndex >= 0 ? CurrentColumnIndex : FirstEditableColumn());

    /// <summary>Begin editing a cell: it becomes the current one, comes into sight, and
    /// its column's editor opens over it.</summary>
    /// <returns>Whether an editor opened: false for a cell that isn't editable, a column
    /// with no editor (a check box), a grid that isn't in a form, and when
    /// <see cref="CellEditing"/> cancelled.</returns>
    public bool BeginEdit(int row, int column) => BeginEdit(row, column, typedText: null);

    private bool BeginEdit(int row, int column, string? typedText)
    {
        // the edit there was ends first; one that can't — a bad value — stays
        if (IsEditing && !EndEdit()) return false;

        if (!IsCellEditable(row, column) || !IsEffectivelyEnabled || FindForm() is not { } form) return false;

        object record = RowItem(row);
        DataGridViewColumn editColumn = Columns[column];

        var args = new DataGridViewCellEditingEventArgs(record, editColumn);
        CellEditing?.Invoke(this, args);

        if (args.Cancel) return false;

        if (editColumn.CreateEditor(record, typedText) is not { } editor) return false;

        // the cell becomes the current one without changing what is selected around it
        if (!IsUnitSelected(row, column)) SelectAt(row, column, SelectGesture.Only);
        else MoveLead(row, column, raise: true);

        ScrollIntoView(row, column);

        _editor = editor;
        _editRecord = record;
        _editColumn = editColumn;

        editor.Font = EffectiveFont;
        editor.PreviewKeyDown += OnEditorPreviewKeyDown;
        editor.LostFocus += OnEditorLostFocus;

        form.AddOverlay(editor);
        PlaceEditor();

        editor.Focus();
        InvalidateVisual();

        return true;
    }

    /// <summary>End editing and write the value.</summary>
    /// <returns>False — the editor holds no value of the column, and stays open;
    /// true — the edit ended, or there was none.</returns>
    public bool EndEdit()
    {
        if (_editor is not { } editor || _editRecord is not { } record || _editColumn is not { } column) return true;

        if (!column.TryGetEditedValue(editor, record, out object? value)) return false;

        CloseEditor();

        // the record may have left the rows while it was edited
        if (DisplayIndexOf(record) >= 0) WriteCell(record, column, value);

        return true;
    }

    /// <summary>End editing and keep the value the cell had.</summary>
    public void CancelEdit() => CloseEditor();

    /// <summary>Write a value into a cell, as an edit does: through
    /// <see cref="CellValueChanging"/> and <see cref="CellValueChanged"/>.</summary>
    /// <returns>Whether the value was written: false for a column that isn't editable,
    /// and when a handler refused it.</returns>
    public bool SetCellValue(int row, int column, object? value)
    {
        if (!IsCellEditable(row, column)) return false;

        return WriteCell(RowItem(row), Columns[column], value);
    }

    private bool WriteCell(object record, DataGridViewColumn column, object? value)
    {
        object? old = column.Value(record);

        // the value it has already: nothing to tell
        if (Equals(old, value)) return true;

        var changing = new DataGridViewCellValueChangingEventArgs(record, column, old, value);
        CellValueChanging?.Invoke(this, changing);

        if (changing.Cancel) return false;

        column.SetValue?.Invoke(record, changing.NewValue);

        CellValueChanged?.Invoke(this, new DataGridViewCellEventArgs(record, column));

        // a grid sorted by the column moves the row to its new place; the selection
        // and the lead follow the record there
        if (SortColumnIndex >= 0)
        {
            BuildOrder();
            RestoreSelection(null);
        }

        // Auto widths are measured from the values
        Invalidate();

        return true;
    }

    /// <summary>A click on a cell, or Space on it, for a column that changes the value
    /// at once — a check box. False — the cell has no such action.</summary>
    private bool ActivateCell(int row, int column)
    {
        if (!IsCellEditable(row, column)) return false;

        object record = RowItem(row);
        DataGridViewColumn cellColumn = Columns[column];

        if (!cellColumn.TryActivate(record, out object? value)) return false;

        WriteCell(record, cellColumn, value);
        return true;
    }

    private int FirstEditableColumn()
    {
        for (int column = 0; column < Columns.Count; column++)
            if (IsCellEditable(Math.Max(SelectedIndex, 0), column))
                return column;

        return -1;
    }

    private void CloseEditor()
    {
        if (_editor is not { } editor) return;

        // cleared first: removing the editor takes its focus, and the lost focus
        // would end the edit a second time
        _editor = null;
        _editRecord = null;
        _editColumn = null;

        editor.PreviewKeyDown -= OnEditorPreviewKeyDown;
        editor.LostFocus -= OnEditorLostFocus;

        // the focus comes back to the grid before the editor leaves: the keyboard goes
        // on in the grid. Not when the focus went elsewhere — a click on another control
        if (editor is IInputElement { IsFocused: true }) Focus();

        editor.FindForm()?.RemoveOverlay(editor);
        InvalidateVisual();
    }

    /// <summary>Put the editor over its cell: after a scroll, a resize, the grid moving
    /// in its window. A cell not wholly in sight hides it — an overlay isn't clipped
    /// by the grid, and would stand over the header or past the grid's edge.</summary>
    private void PlaceEditor()
    {
        if (_editor is not { } editor) return;

        // its column taken out of Columns: nowhere to stand
        if (EditingCell is not { } cell)
        {
            editor.IsVisible = false;
            return;
        }

        EnsureLayout();

        Rectangle body = BodyBounds;
        Point origin = GetAbsolutePosition();

        float left = body.X + ColumnLeft(cell.Column) - _scrollX - _overscroll.X;
        float top = body.Y + cell.Row * RowHeight - _scrollY - _overscroll.Y;
        float width = _widths[cell.Column];

        bool inSight =
            top >= body.Y - 0.5f && top + RowHeight <= body.Y + body.Height + 0.5f &&
            left >= body.X - 0.5f && left + width <= body.X + body.Width + 0.5f;

        editor.IsVisible = inSight;

        if (!inSight) return;

        editor.Position = new Point(origin.X + left, origin.Y + top);

        if (editor.Width != width || editor.Height != RowHeight)
        {
            editor.Width = width;
            editor.Height = RowHeight;
        }
    }

    /// <summary>The editor's own keys come to it first; Enter, Tab and Escape end the
    /// edit before it sees them — Enter would press the form's default button. Not
    /// while its drop-down is open: those keys are the drop-down's.</summary>
    private void OnEditorPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_editor is null || IsDropDownOpen(_editor)) return;

        switch (e.Key)
        {
            case Key.Escape:
                CancelEdit();
                e.Handled = true;
                break;

            case Key.Enter:
                // committed, the edit goes down a row as in a spreadsheet; a bad value
                // keeps it where it is
                if (EndEdit())
                {
                    int down = Math.Min(SelectedIndex + 1, Items.Count - 1);
                    SelectAt(down, CurrentColumnIndex, SelectGesture.Only);
                    ScrollIntoView(down, CurrentColumnIndex);
                }

                e.Handled = true;
                break;

            case Key.Tab:
                if (EndEdit())
                    EditNext(backwards: e.Modifiers.HasFlag(KeyModifiers.Shift));

                e.Handled = true;
                break;
        }
    }

    /// <summary>Go on to the next editable cell with an editor — across the row, then to
    /// the next row — and edit it.</summary>
    private void EditNext(bool backwards)
    {
        int columns = Columns.Count;
        int row = SelectedIndex;
        int column = CurrentColumnIndex;

        if (row < 0 || column < 0 || columns == 0) return;

        int step = backwards ? -1 : 1;

        for (int i = 0; i < Items.Count * columns; i++)
        {
            column += step;

            if (column >= columns)
            {
                column = 0;
                row++;
            }
            else if (column < 0)
            {
                column = columns - 1;
                row--;
            }

            if (row < 0 || row >= Items.Count) return;

            if (IsCellEditable(row, column) && BeginEdit(row, column)) return;
        }
    }

    private void OnEditorLostFocus(object? sender, EventArgs e)
    {
        // a value that isn't one is dropped rather than left in an editor nobody is in
        if (!EndEdit()) CancelEdit();
    }

    private static bool IsDropDownOpen(UIElement editor) => editor switch
    {
        ComboBox combo => combo.IsDropDownOpen,
        DateTimePicker picker => picker.IsDropDownOpen,
        CheckedComboBox list => list.IsDropDownOpen,
        _ => false,
    };

    /// <summary>Typing on the grid begins editing the current cell with what is typed —
    /// a text column's; the other kinds don't take text this way.</summary>
    protected override void OnTextInput(char c)
    {
        base.OnTextInput(c);

        // a space toggles, a control character is no text
        if (IsEditing || char.IsControl(c) || char.IsWhiteSpace(c)) return;

        int row = SelectedIndex;
        int column = CurrentColumnIndex >= 0 ? CurrentColumnIndex : FirstEditableColumn();

        if (row < 0 || !IsCellEditable(row, column)) return;

        // only a column edited as text takes the character: its editor is a text box
        if (!Columns[column].TakesTypedText) return;

        BeginEdit(row, column, c.ToString());
    }

    protected override void OnDetached()
    {
        // the overlay stays in the form the grid left: it goes with it
        CancelEdit();

        base.OnDetached();
    }
}
