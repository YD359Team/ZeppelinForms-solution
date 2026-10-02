namespace ZeppelinForms.Accessibility;

/// <summary>What an element is to a screen reader: the kind of thing it announces,
/// and the keys and gestures it offers for it.</summary>
/// <remarks>
/// The set is the common part of UI Automation control types, ARIA roles, AT-SPI
/// roles and Android class names, so that every bridge maps a role to one of its
/// own without guessing. A bridge without an exact match takes the nearest one —
/// a Switch is a toggle button with an "on/off" state where there is no switch.
/// </remarks>
public enum AccessibilityRole
{
    /// <summary>A layout container with no meaning of its own. Bridges may leave it
    /// out and lift its children one level up.</summary>
    None,

    Window,
    Dialog,

    /// <summary>A titled or otherwise meaningful group of elements.</summary>
    Group,

    /// <summary>Static text.</summary>
    Text,

    /// <summary>A heading; its level is <see cref="AccessibilityPeer.HeadingLevel"/>.</summary>
    Heading,

    Link,
    Image,
    Button,
    ToggleButton,
    SplitButton,
    CheckBox,
    RadioButton,
    Switch,

    /// <summary>An editable text field.</summary>
    TextBox,

    /// <summary>A number with step buttons.</summary>
    SpinButton,

    Slider,
    ProgressBar,
    ScrollBar,
    ComboBox,
    List,
    ListItem,
    Tree,
    TreeItem,
    Table,
    Row,
    Cell,
    ColumnHeader,
    TabList,
    Tab,
    TabPanel,
    MenuBar,
    Menu,
    MenuItem,
    Separator,
    Calendar,
    ToolTip,
}