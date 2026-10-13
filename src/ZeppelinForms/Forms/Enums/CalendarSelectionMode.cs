namespace ZeppelinForms.Forms.Enums;

/// <summary>What a click on a calendar's day picks.</summary>
public enum CalendarSelectionMode
{
    /// <summary>One day: <see cref="Controls.Calendar.SelectedDate"/>.</summary>
    Single,

    /// <summary>A range of days: the first click picks one end, the second the other.
    /// <see cref="Controls.Calendar.RangeStart"/> and <see cref="Controls.Calendar.RangeEnd"/>.</summary>
    Range,
}