# Accessibility

## Screen readers

Every element has an **accessibility peer** — its role, name, value, states and actions —
made the first time something asks for it. The built-in controls know theirs: a list box is a
list of list items, a data grid a table of rows and cells, a tree view a tree.

| Platform | Bridge |
| -------- | ------ |
| Windows | UI Automation: Narrator, NVDA, JAWS |
| Linux | AT-SPI 2 on the accessibility bus: Orca, on X11 and Wayland |
| Browser | An ARIA mirror of the tree beside the canvas, for the browser's screen readers |
| Android | Virtual views of the surface, as Android's custom views give them: TalkBack |

On Linux the application joins the desktop's accessibility bus as it starts, as GTK and Qt
applications do; nothing more happens until a screen reader reads its tree. A text field
reads by characters, words and lines, and what is typed into it is echoed. `NO_AT_BRIDGE=1`
in the environment keeps the application off the bus. On Wayland a screen reader gets the
elements' places within their window: the compositor tells no one where windows are.

On Android the forms are a tree of virtual views under the surface, numbered nodes an
accessibility service reads one by one. With TalkBack on, a finger moving over the screen
reads the element under it; a swipe moves to the next one; a double tap does what a tap on
the element does — presses a button, toggles a check box, focuses a field and opens the
keyboard. Swiping up and down adjusts a slider, a field echoes what is typed, a dialog says
its title as it opens, and the windows under a modal dialog are out of reach. Nothing is
made until a service reads the tree.

The name is worked out from what the control shows — a button's text, a check box's
caption, the label in front of a field. Where nothing on screen says it, give it:

```csharp
var search = new TextBox { AccessibleName = "Search orders" };
var icon = new SvgIcon { PathData = "…", IsAccessibilityHidden = true };    // decoration
var status = new Label { LiveSetting = AccessibilityLiveSetting.Polite };   // changes are read out
```

| Member | |
| ------ | - |
| `AccessibleName`, `AccessibleDescription` | What a screen reader says |
| `AccessibleRole` | A role other than the control's own |
| `IsAccessibilityHidden` | Left out of the tree: decoration |
| `LiveSetting` | Changes of the element are announced |
| `CreateAccessibilityPeer()` | Overridden by a control of your own to give its peer |

### The audit

F12 opens the inspector; its audit lists what a screen reader or a keyboard user would trip
over — a control with no name, a picture with no name that isn't hidden as decoration,
text below the WCAG contrast minimum, a target smaller than 24×24. From code:

```csharp
foreach (AccessibilityIssue issue in AccessibilityAudit.Run(form))
    Console.WriteLine(issue);
```

## The keyboard

Every control is reachable with Tab and Shift+Tab, in `TabIndex` order, and works from the
keyboard: lists and grids with the arrows, Home, End, PageUp and PageDown, a tree with Left
and Right, a menu with the arrows and Escape. The focus ring is shown only while the user
works from the keyboard (`:focus-visible` in style sheets), not after a mouse click.

Access keys: `&` in a caption underlines the next letter and makes Alt with it a shortcut —
on by default for menu items, `UseMnemonic = true` for other controls.

```csharp
var save = new Button { Text = "&Save", UseMnemonic = true };   // Alt+S
```

A caption in another script answers its own letter in the user's layout: `&Файл` is Alt+Ф
on every platform, as Alt with the key's Latin letter answers a Latin caption.

## High contrast and text scale

The system's high contrast mode takes over every theme with its own colors — opt out with
`App.RespectHighContrast = false`. `App.IsHighContrastActive` tells whether it is on.

| Platform | Contrast is on | Its colors |
| -------- | -------------- | ---------- |
| Windows | a contrast theme | the theme's |
| Browser | forced colors | the CSS system colors |
| Linux | "contrast" of the desktop portal | KDE's color scheme; elsewhere the built-in black or white palette, by the dark mode |
| Android | high-contrast text | the built-in palette |

`App.UseSystemTextScale()` follows the system's text size setting; `App.TextScale` is the
factor in effect, and `App.TextScaleChanged` tells when it changes.

| Platform | The setting |
| -------- | ----------- |
| Windows | Accessibility › Text size |
| Android | the font scale |
| Linux | GNOME's text scaling factor ("Large Text"); KDE's general font against its 10 points |
| Browser | the browser's font size setting (the page zoom is the display scale, not this) |

## Motion

With the system's reduced-motion setting (`Motion.Preference`), transitions and animations
are instant; scroll inertia and loading indicators keep moving.
