using ZeppelinForms.Accessibility;

namespace ZeppelinForms.Linux.Accessibility;

/// <summary>The names and numbers of AT-SPI 2: the interfaces, the roles and the states as
/// atspi-constants.h numbers them, and the translation of the core's roles and states.</summary>
/// <remarks>
/// The numbers are the wire's: an assistive technology reads a role as a number and a
/// state set as two 32-bit words of bits. They are fixed by the protocol and only grow
/// at the end, so they are written out here rather than read from a library — the
/// bridge has no native dependency, as the rest of the D-Bus client has none.
/// </remarks>
internal static class AtSpi
{
    public const string AccessibleInterface = "org.a11y.atspi.Accessible";
    public const string ComponentInterface = "org.a11y.atspi.Component";
    public const string ActionInterface = "org.a11y.atspi.Action";
    public const string ValueInterface = "org.a11y.atspi.Value";
    public const string TextInterface = "org.a11y.atspi.Text";
    public const string ApplicationInterface = "org.a11y.atspi.Application";
    public const string CacheInterface = "org.a11y.atspi.Cache";
    public const string SocketInterface = "org.a11y.atspi.Socket";
    public const string ObjectEvents = "org.a11y.atspi.Event.Object";
    public const string WindowEvents = "org.a11y.atspi.Event.Window";
    public const string PropertiesInterface = "org.freedesktop.DBus.Properties";
    public const string IntrospectableInterface = "org.freedesktop.DBus.Introspectable";

    /// <summary>The registry: the desktop every application embeds itself in.</summary>
    public const string RegistryName = "org.a11y.atspi.Registry";

    /// <summary>Where the objects live: the application at <see cref="RootPath"/>, every
    /// other at a number under <see cref="AccessiblePrefix"/>.</summary>
    public const string AccessiblePrefix = "/org/a11y/atspi/accessible";

    public const string RootPath = "/org/a11y/atspi/accessible/root";

    public const string CachePath = "/org/a11y/atspi/cache";

    /// <summary>The reference to no object: an empty name and this path.</summary>
    public const string NullPath = "/org/a11y/atspi/null";

    // ===== coordinates (AtspiCoordType) =====

    public const uint CoordScreen = 0;
    public const uint CoordWindow = 1;
    public const uint CoordParent = 2;

    // ===== layers (AtspiComponentLayer) =====

    public const uint LayerWidget = 3;
    public const uint LayerWindow = 7;

    // ===== text boundaries (AtspiTextBoundaryType) and granularities =====

    public const uint BoundaryChar = 0;
    public const uint BoundaryWordStart = 1;
    public const uint BoundaryWordEnd = 2;
    public const uint BoundarySentenceStart = 3;
    public const uint BoundarySentenceEnd = 4;
    public const uint BoundaryLineStart = 5;
    public const uint BoundaryLineEnd = 6;

    public const uint GranularityChar = 0;
    public const uint GranularityWord = 1;
    public const uint GranularitySentence = 2;
    public const uint GranularityLine = 3;
    public const uint GranularityParagraph = 4;

    // ===== live regions (AtspiLive) =====

    public const int LivePolite = 1;
    public const int LiveAssertive = 2;

    // ===== roles (AtspiRole) =====

    public const uint RoleCalendar = 5;
    public const uint RoleCheckBox = 7;
    public const uint RoleColumnHeader = 10;
    public const uint RoleComboBox = 11;
    public const uint RoleDialog = 16;
    public const uint RoleFrame = 23;
    public const uint RoleImage = 27;
    public const uint RoleLabel = 29;
    public const uint RoleListItem = 32;
    public const uint RoleMenu = 33;
    public const uint RoleMenuBar = 34;
    public const uint RoleMenuItem = 35;
    public const uint RolePageTab = 37;
    public const uint RolePageTabList = 38;
    public const uint RolePanel = 39;
    public const uint RolePasswordText = 40;
    public const uint RoleProgressBar = 42;
    public const uint RolePushButton = 43;
    public const uint RoleRadioButton = 44;
    public const uint RoleScrollBar = 48;
    public const uint RoleSeparator = 50;
    public const uint RoleSlider = 51;
    public const uint RoleSpinButton = 52;
    public const uint RoleTable = 55;
    public const uint RoleTableCell = 56;
    public const uint RoleText = 61;
    public const uint RoleToggleButton = 62;
    public const uint RoleToolTip = 64;
    public const uint RoleTree = 65;
    public const uint RoleApplication = 75;
    public const uint RoleEntry = 79;
    public const uint RoleHeading = 83;
    public const uint RoleLink = 88;
    public const uint RoleTableRow = 90;
    public const uint RoleTreeItem = 91;
    public const uint RoleListBox = 98;
    public const uint RoleGrouping = 99;

    /// <summary>The core's role as AT-SPI's. A text field that holds a password is
    /// password text, and one of several lines is text rather than an entry, as GTK
    /// exposes them. A switch is a toggle button: AT-SPI's own switch role is newer than
    /// the screen readers of most desktops in use, and they would announce it as unknown.</summary>
    public static uint Role(AccessibilityRole role, AccessibilityStates states) => role switch
    {
        AccessibilityRole.None => RolePanel,
        AccessibilityRole.Window => RoleFrame,
        AccessibilityRole.Dialog => RoleDialog,
        AccessibilityRole.Group => RoleGrouping,
        AccessibilityRole.Text => RoleLabel,
        AccessibilityRole.Heading => RoleHeading,
        AccessibilityRole.Link => RoleLink,
        AccessibilityRole.Image => RoleImage,
        AccessibilityRole.Button => RolePushButton,
        AccessibilityRole.ToggleButton => RoleToggleButton,
        AccessibilityRole.SplitButton => RolePushButton,
        AccessibilityRole.CheckBox => RoleCheckBox,
        AccessibilityRole.RadioButton => RoleRadioButton,
        AccessibilityRole.Switch => RoleToggleButton,
        AccessibilityRole.TextBox when states.HasFlag(AccessibilityStates.Protected) => RolePasswordText,
        AccessibilityRole.TextBox when states.HasFlag(AccessibilityStates.Multiline) => RoleText,
        AccessibilityRole.TextBox => RoleEntry,
        AccessibilityRole.SpinButton => RoleSpinButton,
        AccessibilityRole.Slider => RoleSlider,
        AccessibilityRole.ProgressBar => RoleProgressBar,
        AccessibilityRole.ScrollBar => RoleScrollBar,
        AccessibilityRole.ComboBox => RoleComboBox,
        AccessibilityRole.List => RoleListBox,
        AccessibilityRole.ListItem => RoleListItem,
        AccessibilityRole.Tree => RoleTree,
        AccessibilityRole.TreeItem => RoleTreeItem,
        AccessibilityRole.Table => RoleTable,
        AccessibilityRole.Row => RoleTableRow,
        AccessibilityRole.Cell => RoleTableCell,
        AccessibilityRole.ColumnHeader => RoleColumnHeader,
        AccessibilityRole.TabList => RolePageTabList,
        AccessibilityRole.Tab => RolePageTab,
        AccessibilityRole.TabPanel => RolePanel,
        AccessibilityRole.MenuBar => RoleMenuBar,
        AccessibilityRole.Menu => RoleMenu,
        AccessibilityRole.MenuItem => RoleMenuItem,
        AccessibilityRole.Separator => RoleSeparator,
        AccessibilityRole.Calendar => RoleCalendar,
        AccessibilityRole.ToolTip => RoleToolTip,
        _ => RolePanel,
    };

    /// <summary>A role's name as atspi_role_get_name gives it: what GetRoleName answers.
    /// The localized name is the same — screen readers translate the role themselves.</summary>
    public static string RoleName(uint role) => role switch
    {
        RoleCalendar => "calendar",
        RoleCheckBox => "check box",
        RoleColumnHeader => "column header",
        RoleComboBox => "combo box",
        RoleDialog => "dialog",
        RoleFrame => "frame",
        RoleImage => "image",
        RoleLabel => "label",
        RoleListItem => "list item",
        RoleMenu => "menu",
        RoleMenuBar => "menu bar",
        RoleMenuItem => "menu item",
        RolePageTab => "page tab",
        RolePageTabList => "page tab list",
        RolePanel => "panel",
        RolePasswordText => "password text",
        RoleProgressBar => "progress bar",
        RolePushButton => "push button",
        RoleRadioButton => "radio button",
        RoleScrollBar => "scroll bar",
        RoleSeparator => "separator",
        RoleSlider => "slider",
        RoleSpinButton => "spin button",
        RoleTable => "table",
        RoleTableCell => "table cell",
        RoleText => "text",
        RoleToggleButton => "toggle button",
        RoleToolTip => "tool tip",
        RoleTree => "tree",
        RoleApplication => "application",
        RoleEntry => "entry",
        RoleHeading => "heading",
        RoleLink => "link",
        RoleTableRow => "table row",
        RoleTreeItem => "tree item",
        RoleListBox => "list box",
        RoleGrouping => "grouping",
        _ => "unknown",
    };

    // ===== states (AtspiStateType): bit numbers in the two words of a state set =====

    public const int StateActive = 1;
    public const int StateBusy = 3;
    public const int StateChecked = 4;
    public const int StateCollapsed = 5;
    public const int StateEditable = 7;
    public const int StateEnabled = 8;
    public const int StateExpandable = 9;
    public const int StateExpanded = 10;
    public const int StateFocusable = 11;
    public const int StateFocused = 12;
    public const int StateModal = 16;
    public const int StateMultiLine = 17;
    public const int StateMultiSelectable = 18;
    public const int StateSelectable = 22;
    public const int StateSelected = 23;
    public const int StateSensitive = 24;
    public const int StateShowing = 25;
    public const int StateSingleLine = 26;
    public const int StateVisible = 30;
    public const int StateIndeterminate = 32;
    public const int StateInvalidEntry = 36;
    public const int StateVisited = 40;
    public const int StateCheckable = 41;
    public const int StateReadOnly = 43;

    /// <summary>The core's states as AT-SPI's state set, a 64-bit mask. Every object
    /// that is in the tree is visible and showing: hidden elements have no peer in it.</summary>
    public static ulong States(AccessibilityRole role, AccessibilityStates states)
    {
        ulong set = Bit(StateVisible) | Bit(StateShowing);

        if (!states.HasFlag(AccessibilityStates.Disabled))
            set |= Bit(StateEnabled) | Bit(StateSensitive);

        if (states.HasFlag(AccessibilityStates.Focusable)) set |= Bit(StateFocusable);
        if (states.HasFlag(AccessibilityStates.Focused)) set |= Bit(StateFocused);
        if (states.HasFlag(AccessibilityStates.Checked)) set |= Bit(StateChecked);
        if (states.HasFlag(AccessibilityStates.Mixed)) set |= Bit(StateIndeterminate);
        if (states.HasFlag(AccessibilityStates.Expanded)) set |= Bit(StateExpandable) | Bit(StateExpanded);
        if (states.HasFlag(AccessibilityStates.Collapsed)) set |= Bit(StateExpandable) | Bit(StateCollapsed);
        if (states.HasFlag(AccessibilityStates.Selected)) set |= Bit(StateSelected);
        if (states.HasFlag(AccessibilityStates.Selectable)) set |= Bit(StateSelectable);
        if (states.HasFlag(AccessibilityStates.MultiSelectable)) set |= Bit(StateMultiSelectable);
        if (states.HasFlag(AccessibilityStates.ReadOnly)) set |= Bit(StateReadOnly);
        if (states.HasFlag(AccessibilityStates.Invalid)) set |= Bit(StateInvalidEntry);
        if (states.HasFlag(AccessibilityStates.Busy)) set |= Bit(StateBusy);
        if (states.HasFlag(AccessibilityStates.Visited)) set |= Bit(StateVisited);
        if (states.HasFlag(AccessibilityStates.Modal)) set |= Bit(StateModal);

        if (role is AccessibilityRole.CheckBox or AccessibilityRole.RadioButton or
            AccessibilityRole.ToggleButton or AccessibilityRole.Switch)
        {
            set |= Bit(StateCheckable);
        }

        if (role is AccessibilityRole.TextBox)
        {
            set |= states.HasFlag(AccessibilityStates.Multiline) ? Bit(StateMultiLine) : Bit(StateSingleLine);

            if (!states.HasFlag(AccessibilityStates.ReadOnly))
                set |= Bit(StateEditable);
        }

        return set;
    }

    /// <summary>A state's bit in the 64-bit set.</summary>
    public static ulong Bit(int state) => 1UL << state;

    /// <summary>The set as the wire carries it: two 32-bit words, the low one first.</summary>
    public static uint[] Words(ulong set) => [(uint)set, (uint)(set >> 32)];

    /// <summary>The name of a state in a StateChanged event: "checked", "expanded".
    /// Null for one that isn't announced.</summary>
    public static string? StateName(int state) => state switch
    {
        StateActive => "active",
        StateBusy => "busy",
        StateChecked => "checked",
        StateCollapsed => "collapsed",
        StateEditable => "editable",
        StateEnabled => "enabled",
        StateExpandable => "expandable",
        StateExpanded => "expanded",
        StateFocusable => "focusable",
        StateFocused => "focused",
        StateModal => "modal",
        StateMultiSelectable => "multiselectable",
        StateSelectable => "selectable",
        StateSelected => "selected",
        StateSensitive => "sensitive",
        StateIndeterminate => "indeterminate",
        StateInvalidEntry => "invalid-entry",
        StateVisited => "visited",
        StateCheckable => "checkable",
        StateReadOnly => "read-only",
        _ => null,
    };

    // ===== actions =====

    /// <summary>The actions an object offers, in the order the Action interface numbers
    /// them, with the names AT-SPI's clients know: "click", "toggle", "expand".</summary>
    public static IReadOnlyList<(AccessibilityActions Action, string Name)> Actions(AccessibilityActions actions)
    {
        var list = new List<(AccessibilityActions, string)>();

        if (actions.HasFlag(AccessibilityActions.Invoke)) list.Add((AccessibilityActions.Invoke, "click"));
        if (actions.HasFlag(AccessibilityActions.Toggle)) list.Add((AccessibilityActions.Toggle, "toggle"));
        if (actions.HasFlag(AccessibilityActions.Select)) list.Add((AccessibilityActions.Select, "select"));
        if (actions.HasFlag(AccessibilityActions.Expand)) list.Add((AccessibilityActions.Expand, "expand"));
        if (actions.HasFlag(AccessibilityActions.Collapse)) list.Add((AccessibilityActions.Collapse, "collapse"));

        return list;
    }

    /// <summary>An access key as an action's key binding: "Alt+S" is "&lt;Alt&gt;s" — the
    /// mnemonic part of "mnemonic;sequence;shortcut", which is what Orca reads.</summary>
    public static string KeyBinding(string? accessKey)
    {
        if (string.IsNullOrEmpty(accessKey)) return string.Empty;

        string[] parts = accessKey.Split('+');
        var binding = new System.Text.StringBuilder();

        for (int i = 0; i < parts.Length - 1; i++)
            binding.Append('<').Append(parts[i]).Append('>');

        binding.Append(parts[^1].ToLowerInvariant());

        return binding.Append(";;").ToString();
    }
}
