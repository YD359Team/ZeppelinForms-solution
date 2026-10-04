using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.Forms.Controls;

/// <summary>The calendar from the keyboard: a cursor day the arrows move, and
/// Enter picks. The same keys reach it from a date picker's drop-down.</summary>
/// <remarks>
/// The cursor is separate from the selection, as in every calendar: walking the
/// days must not change the date until the user says so. It starts on the selected
/// date, or today, and moving it past the month's edge turns the month.
/// </remarks>
public partial class Calendar : IInputElement
{
    public bool IsFocused { get; set; }
    public bool TabStop { get; set; } = true;
    public uint TabIndex { get; set; }

    private DateTime? _keyboardDate;

    /// <summary>The cursor was moved by keys forwarded from a drop-down, where the
    /// calendar has no focus of its own to show it by.</summary>
    private bool _cursorShown;

    /// <summary>The day the keyboard is on.</summary>
    internal DateTime KeyboardDate => (_keyboardDate ?? SelectedDate ?? DateTime.Today).Date;

    /// <summary>The cursor moved — for the accessibility peer, which reads the
    /// cursor day out while the calendar has the focus.</summary>
    internal event EventHandler? KeyboardDateChanged;

    /// <summary>The cursor is drawn while focus is visible, or while a drop-down
    /// is being worked from the keyboard.</summary>
    private bool ShowsCursor => IsFocusVisible || _cursorShown;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (HandleNavigationKey(e.Key, e.Modifiers))
            e.Handled = true;
        else
            base.OnKeyDown(e);
    }

    /// <summary>Move the cursor or pick its day: arrows by a day and a week,
    /// PageUp and PageDown by a month — with Ctrl by a year — Home and End to the
    /// month's ends, Enter and Space pick. False — not a calendar key.</summary>
    internal bool HandleNavigationKey(Key key, KeyModifiers modifiers)
    {
        DateTime current = KeyboardDate;
        bool ctrl = modifiers.HasFlag(KeyModifiers.Control);

        // the days run right to left in a right-to-left layout: the arrows follow
        int forward = IsRightToLeft ? -1 : 1;

        DateTime? next = key switch
        {
            Key.Left => current.AddDays(-forward),
            Key.Right => current.AddDays(forward),
            Key.Up => current.AddDays(-7),
            Key.Down => current.AddDays(7),
            Key.PageUp => ctrl ? current.AddYears(-1) : current.AddMonths(-1),
            Key.PageDown => ctrl ? current.AddYears(1) : current.AddMonths(1),
            Key.Home => new DateTime(current.Year, current.Month, 1),
            Key.End => new DateTime(current.Year, current.Month, DateTime.DaysInMonth(current.Year, current.Month)),
            _ => null,
        };

        if (next is { } date)
        {
            MoveCursor(date);
            return true;
        }

        if (key is Key.Enter or Key.Space)
        {
            Pick(current);
            return true;
        }

        return false;
    }

    private void MoveCursor(DateTime date)
    {
        _keyboardDate = date.Date;
        _cursorShown = true;

        // past the edge of the month the month follows the cursor
        _displayMonth = date;

        Invalidate();
        KeyboardDateChanged?.Invoke(this, EventArgs.Empty);
    }
}