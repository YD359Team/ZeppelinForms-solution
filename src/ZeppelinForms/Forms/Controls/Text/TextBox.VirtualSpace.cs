using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Forms.Controls.Text;

/// <summary>
/// The caret past the end of a line — Visual Studio's virtual space. The caret stays at
/// the line's end in the text, and a count of spaces after it says how far to the right
/// it stands; the spaces become text only when something is typed there.
/// </summary>
public partial class TextBox
{
    /// <summary>The caret goes past the end of a line: Right at the end goes on into
    /// the empty room, Up and Down keep their column over short lines, a click beyond a
    /// line's end puts the caret where it was clicked. Typing there fills the gap with
    /// spaces first. Off by default.</summary>
    /// <remarks>
    /// <para>
    /// For a multi-line field without <see cref="WordWrap"/>, left to right: a wrapped
    /// row has no room past its end, and in right-to-left text the room is on the other
    /// side. Elsewhere the setting is kept and does nothing.
    /// </para>
    /// <para>
    /// Only the caret goes there, not a selection: Shift with an arrow selects from the
    /// line's end, as in Visual Studio. Enter adds no spaces — a new line starts clean.
    /// </para>
    /// </remarks>
    public bool AllowCaretPastLineEnd
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            if (!value && _virtualSpaces > 0)
            {
                _virtualSpaces = 0;
                InvalidateVisual();
            }
        }
    }

    /// <summary>How many spaces past the line's end the caret stands; 0 — at a
    /// character of the text, as always.</summary>
    private int _virtualSpaces;

    /// <summary>The caret is being placed in the room past a line: the caret's move
    /// mustn't take the room away.</summary>
    private bool _placingVirtualCaret;

    /// <summary>The caret past the end of its line — for tests and the accessibility peer.</summary>
    internal int VirtualSpaces => _virtualSpaces;

    private bool VirtualSpaceActive =>
        AllowCaretPastLineEnd && IsMultiline && !WordWrap && !IsRightToLeft && PasswordChar is null;

    /// <summary>The width a column of the room takes: a space of the field's font.</summary>
    private float SpaceWidth => Math.Max(1f, TextMeasurer.MeasureLine(" ", EffectiveFont).Width);

    /// <summary>The caret stands at the end of its line, with nothing selected.</summary>
    private bool CaretAtLineEnd()
    {
        if (_document.SelectionLength != 0) return false;

        int caret = _document.CaretIndex;
        string text = _document.Text;

        return caret >= text.Length || text[caret] == '\n';
    }

    /// <summary>The caret moved or the text changed: the room is gone, unless the caret
    /// is being put into it right now.</summary>
    private void ForgetVirtualSpace()
    {
        if (!_placingVirtualCaret) _virtualSpaces = 0;
    }

    /// <summary>The room changed: the caret is seen at once, and Up and Down aim at
    /// where it stands now, not where they last aimed.</summary>
    private void VirtualSpaceChanged()
    {
        _desiredX = null;
        ResetCaretBlink();
        InvalidateVisual();
    }

    /// <summary>Put the caret at an index and that many spaces past it.</summary>
    private void PlaceCaret(int index, int spaces, bool byRows)
    {
        _placingVirtualCaret = true;

        try
        {
            if (byRows) SetCaretByRows(index, extend: false);
            else _document.SetCaret(index);

            _virtualSpaces = spaces;
        }
        finally
        {
            _placingVirtualCaret = false;
        }

        ResetCaretBlink();
        InvalidateVisual();
    }

    /// <summary>The spaces to put in before text typed at the caret, and the room gone:
    /// from here on the caret is in the text again.</summary>
    private string TakeVirtualFill()
    {
        int spaces = VirtualSpaceActive ? _virtualSpaces : 0;
        _virtualSpaces = 0;

        return spaces > 0 ? new string(' ', spaces) : string.Empty;
    }

    /// <summary>Insert typed or pasted text, the gap past the line filled first. One
    /// insertion: one undo takes away both.</summary>
    private void InsertAtCaret(string text) => _document.Insert(TakeVirtualFill() + text);

    /// <summary>Up and Down with the room: the column is kept by its x, and over a
    /// short line the caret stands past its end rather than at it.</summary>
    /// <param name="spacesBefore">The room the caret stood in before the key.</param>
    private void MoveVerticalInVirtualSpace(int delta, int spacesBefore)
    {
        IReadOnlyList<TextRow> rows = Rows(TextBounds.Width);
        var (row, column) = RowOf(rows, _document.CaretIndex);

        int target = row + delta;

        if (target < 0 || target >= rows.Count)
        {
            // the first or the last line: the caret stays where it stood
            _virtualSpaces = spacesBefore;
            return;
        }

        float width = TextBounds.Width;
        float space = SpaceWidth;

        TextLineLayout from = RowLayout(rows, row);
        TextLineLayout to = RowLayout(rows, target);

        _desiredX ??= RowShift(from, width) + from.CaretX(column) + spacesBefore * space;

        (int index, int spaces) = CaretAtX(rows[target], to, _desiredX.Value - RowShift(to, width), space);

        // by rows: the aimed-at x survives the move, as it does under word wrap
        _placingVirtualCaret = true;

        try
        {
            SetCaretByRows(index, extend: false);
            _virtualSpaces = spaces;
        }
        finally
        {
            _placingVirtualCaret = false;
        }

        ResetCaretBlink();
        InvalidateVisual();
    }

    /// <summary>The place in a row nearest to <paramref name="x"/>: a column of the text,
    /// or the row's end and the spaces past it.</summary>
    private static (int Index, int Spaces) CaretAtX(TextRow row, TextLineLayout layout, float x, float space)
    {
        float end = layout.CaretX(row.Text.Length);

        if (x > end + space / 2f)
            return (row.Start + row.Text.Length, (int)MathF.Round((x - end) / space));

        return (row.Start + ColumnAt(row, layout, x), 0);
    }

    /// <summary>The caret's place for a press: past the end of the line it was pressed on,
    /// when the press is beyond the line's last character.</summary>
    private (int Index, int Spaces) CaretFromPoint(Point location)
    {
        int index = IndexFromPoint(location);

        if (!VirtualSpaceActive || IsComposing) return (index, 0);

        Point abs = GetAbsolutePosition();
        Rectangle area = TextBounds;

        float localX = location.X - abs.X - area.X + _scrollOffset;
        float localY = location.Y - abs.Y - area.Y + _verticalOffset;

        IReadOnlyList<TextRow> rows = Rows(area.Width);
        int row = Math.Clamp((int)(localY / LineHeight), 0, rows.Count - 1);

        TextLineLayout layout = RowLayout(rows, row);

        return CaretAtX(rows[row], layout, localX - RowShift(layout, area.Width), SpaceWidth);
    }
}