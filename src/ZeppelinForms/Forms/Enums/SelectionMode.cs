namespace ZeppelinForms.Forms.Enums;

public enum SelectionMode
{
    /// <summary>One row. A click moves the selection.</summary>
    Single,

    /// <summary>A click toggles a row without clearing the others.
    /// No modifiers needed — convenient for a list with checkboxes and on a touch screen.</summary>
    Multiple,

    /// <summary>Like in Explorer: a click replaces the selection,
    /// Ctrl toggles one row, Shift selects a range from the anchor.</summary>
    Extended,
}