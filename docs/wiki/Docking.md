# Docking

`DockHost` (`ZeppelinForms.Forms.Controls.Docking`) holds panels the user arranges, the way
an IDE's are arranged. Panels sit side by side and as tabs, with splitters between them.
They can float in windows of their own over the layout, or be pinned to an edge, where
they slide out while they are in use. The user drags them around by their tabs, and the
whole arrangement is saved and restored as JSON.

```csharp
var host = new DockHost();

var editor = new DockItem("editor", "Program.cs", new TextBox { IsMultiline = true });
var files = new DockItem("files", "Explorer", tree) { CanClose = false };
var output = new DockItem("output", "Output", log) { HideOnClose = true };

host.DockTo(editor);                                      // a tab of the layout
host.DockTo(files, DockPosition.Left, size: 0.25f);       // a quarter on the left
host.DockTo(output, DockPosition.Bottom, editor, 0.3f);   // under the editor alone

Content = host;
```

## Items

A `DockItem` is a panel: its `Id`, the `Title` and `Icon` of its tab, a `ToolTip`, and the
`Content` it shows. Whatever the item stands for, a document or a tool window, belongs to
the application and is kept in `Tag`.

- The content fills its panel, whatever its alignment says.
- Only the selected tab of a group is in the tree. The others are neither measured nor
  drawn, and they keep their state until they are selected again.
- `Id` names the item in a saved layout, and it is unique within its host.
- `State` says where the item is: `Docked`, `Floating`, `AutoHidden` or `Hidden`.
- `IsActive` says whether the item was the last one used.

The flags belong to the user. `CanClose`, `CanFloat` and `CanAutoHide` remove the matching
buttons and menu items, but the host's methods still do what they are told. With
`HideOnClose`, closing an item hides it instead of taking it out of the host: that is how
a tool window behaves when a menu shows it again.

## Placing

| Method | What it does |
|---|---|
| `DockTo(item, position, target, size)` | `Tabbed` makes the item a tab of the target's group. `Left`, `Top`, `Right` or `Bottom` puts it beside the target, taking `size` of the target's extent. Without a target, `Tabbed` adds the item to the group that was active last, and a side puts it beside the whole layout. |
| `Float(item, bounds)` | Puts the item in a floating window, in the host's coordinates. Without bounds, the window goes where the item last floated, or to the middle. |
| `AutoHide(item, side)` | Pins the item to an edge. Without a side, it goes to the edge nearest to where it was docked. |
| `Hide(item)` | Hides the item. It stays in `Items`, and the host remembers its place. |
| `Show(item)` | Brings a hidden item back to where it was: docked, floating or on its edge. An auto-hidden item slides out, and a tab is selected. Either way, the item becomes active. |
| `Close(item)` | Closes the item as its close button does. `ItemClosing` may cancel it; otherwise the item is hidden (with `HideOnClose`) or removed from the host. |

An item that is not in the host yet is added by any of these methods. An item belongs to
one host at a time: once it is closed and removed, it is free to go to another.

A hidden item goes back to the group it left while that group is still in the layout. If
the group is gone, the item goes to the side it was on. When several items leave the same
group and come back one by one, they meet again in one group.

## The user's hands

- **Dragging a tab** up or down off its strip turns the strip's drag into the host's.
  A compass appears over the group under the pointer: its middle makes the item a tab of
  that group, and its four sides split the group. Four more buttons sit on the host's own
  edges. Over a group's tabs, dropping the item makes it a tab without aiming for the
  middle button. Dropping it anywhere else floats it where it was dropped. Esc cancels the
  drag, wherever the focus is.
- **A floating window** is moved by its title bar and resized by its frame. Dragging it by
  the title shows the compass too, and the whole window docks where it is dropped. A
  double click on the title docks the window back.
- **A double click on a tab** floats the item.
- **The pin** in a group's header pins the whole group to its edge. Clicking an edge's
  button slides the panel out; a press anywhere else, or Esc, slides it back. The panel's
  own pin docks it back, and its inner edge resizes it.
- **The menu of a tab** offers *Float* (or *Dock*), *Auto hide*, *Close* and *Close
  others*, as far as the item's flags allow.
- **The splitters** move the shares of their two neighbours, and the other panels keep
  theirs.

The floating windows are elements inside the host, not windows of the system, so they
look and behave the same on every platform, including the browser and a phone. A window
always keeps its title bar inside the host, where it can still be grabbed.

## Events

- `ActiveItemChanged` is raised when the item last used changes. That happens when a tab
  is clicked, when the content of a panel is pressed, or when `Show` is called. The
  active group is outlined in `AccentColor`.
- `ItemClosing` is raised before an item closes. Set `Cancel` to keep it, for example to
  ask about unsaved changes first.
- `ItemClosed` is raised after an item closes, whether it was hidden or removed.
- `LayoutChanged` is raised when the arrangement changes: an item moved, or a splitter or
  a window was dragged. This is the moment to save the layout, if it is saved as it goes.

## Saving the layout

```csharp
string json = host.SaveLayout();
settings.DockLayout = json;

// at the next start, with the items made again
host.LoadLayout(settings.DockLayout, id => id switch
{
    "editor" => new DockItem("editor", "Program.cs", editor),
    "files" => new DockItem("files", "Explorer", tree),
    _ => null,   // an id the application no longer has is left out
});
```

The JSON records the splits and their shares, the groups and their tabs, the floating
windows and their bounds, the pinned items and how far each slides out, the hidden items,
and the active item. Items are named by `Id`. On loading:

- an item is either the host's own item with that id, or the one `resolve` makes for it;
- an id that neither of them knows is left out;
- an item of the host that the layout doesn't name is hidden.

The layout is read through before anything changes. Text that is not a layout throws a
`FormatException`, and the host stays as it was. The writer and the reader are written by
hand, without reflection, so they work the same after trimming and with AOT.

## Looks

The panels' colors are styled properties of the host: `SplitterColor`,
`SplitterHoverColor`, `PanelBorderColor`, `PanelBackgroundColor`, `AccentColor`,
`HeaderColor`, `ActiveHeaderColor`, `ButtonHoverColor`, `PreviewColor` and `CompassColor`.
The themes set all of them, high contrast included. The tabs are a
[`TabStrip`](Controls), and the theme's rules for the strip apply to them. The splitters'
width is `SplitterThickness`.

```css
DockHost { AccentColor: #7C4DFF; HeaderColor: #F3F0FA; }
```

## TabStrip on its own

The tear-off is a `TabStrip` feature, so an application's own strip can use it as well.
With `CanDragOut` set, a tab dragged up or down off the strip raises `TabDraggedOut` with
the tab and the pointer. The strip then lets go of the drag, and a handler that takes the
pointer carries it on until the button is released. A browser-like application opens a
window for the tab there.
