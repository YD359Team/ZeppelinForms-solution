namespace ZeppelinForms.Accessibility;

/// <summary>The states an element reports alongside its role.</summary>
[Flags]
public enum AccessibilityStates
{
    None = 0,

    /// <summary>Can take keyboard focus.</summary>
    Focusable = 1 << 0,
    Focused = 1 << 1,

    /// <summary>Disabled itself or by an ancestor.</summary>
    Disabled = 1 << 2,

    Checked = 1 << 3,

    /// <summary>The third state of a check box: neither checked nor unchecked.</summary>
    Mixed = 1 << 4,

    /// <summary>Can be expanded or collapsed, and is expanded now.</summary>
    Expanded = 1 << 5,

    /// <summary>Can be expanded or collapsed, and is collapsed now. Separate from
    /// the absence of <see cref="Expanded"/>: "collapsed" is announced, "can't
    /// expand at all" is not.</summary>
    Collapsed = 1 << 6,

    Selected = 1 << 7,
    Selectable = 1 << 8,

    /// <summary>The container allows more than one selected item.</summary>
    MultiSelectable = 1 << 9,

    ReadOnly = 1 << 10,

    /// <summary>The content is a password and must not be read out.</summary>
    Protected = 1 << 11,

    /// <summary>The value failed validation.</summary>
    Invalid = 1 << 12,

    /// <summary>Work is going on whose end is unknown — an indeterminate progress.</summary>
    Busy = 1 << 13,

    Multiline = 1 << 14,

    /// <summary>A link already followed.</summary>
    Visited = 1 << 15,

    /// <summary>A window that blocks the others until it closes.</summary>
    Modal = 1 << 16,
}

/// <summary>What an assistive technology can do with an element on the user's behalf.</summary>
[Flags]
public enum AccessibilityActions
{
    None = 0,
    Invoke = 1 << 0,
    Toggle = 1 << 1,
    Select = 1 << 2,
    Expand = 1 << 3,
    Collapse = 1 << 4,
    SetValue = 1 << 5,
    Focus = 1 << 6,
    ScrollIntoView = 1 << 7,
}

/// <summary>How a change of an element is announced without the user moving to it.</summary>
public enum AccessibilityLiveSetting
{
    /// <summary>Not announced: the user reads it when they get there.</summary>
    Off,

    /// <summary>Announced when the screen reader is done with what it is saying.</summary>
    Polite,

    /// <summary>Announced at once, interrupting: for errors and alerts only.</summary>
    Assertive,
}

/// <summary>Which part of a peer changed, in <see cref="AccessibilityEvents.PropertyChanged"/>.</summary>
public enum AccessibilityProperty
{
    Name,
    Description,
    Value,
    Range,
    States,
    Bounds,
}

/// <summary>A numeric value within limits: a slider, a progress, a spin button.</summary>
public readonly record struct RangeInfo(double Minimum, double Maximum, double Value, double SmallChange);

/// <summary>The place of an item among its siblings, one-based: "3 of 10".</summary>
public readonly record struct SetPosition(int Index, int Count);