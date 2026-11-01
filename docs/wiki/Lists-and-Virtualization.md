# Lists and virtualization

`ListBox`, `CheckedListBox` and `DragList` are `ItemsControl`s: panels whose children are
made from data. `Items` holds the data; a container — a label by default — is made for
each item it shows.

```csharp
var list = new ListBox
{
    SelectionMode = SelectionMode.Extended,
    OverflowY = Overflow.Auto,
    Height = 300,
};

list.Items.AddRange(customers);
list.SelectionChanged += (_, _) => details.Show(list.SelectedItem);
```

| Member | |
| ------ | - |
| `Items` | The data. With a [`DataSource`](Data-Binding#data-sources) set, a mirror of it |
| `ItemTemplate` | `Func<object, UIElement>`: the container of an item. Null — a label with its `ToString()` |
| `SelectionMode` | `Single`, `Multiple` (a click toggles), `Extended` (Ctrl toggles, Shift selects a range) |
| `SelectedIndex`, `SelectedItem` | The lead item |
| `SelectedIndices`, `SelectedItems` | Every selected item |
| `SetSelected`, `SelectAll`, `ClearSelection` | From code |

```csharp
list.ItemTemplate = item => new StackPanel
{
    Orientation = Orientation.Horizontal,
    Spacing = 8,
    Children = { new SvgIcon { PathData = "M12 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8z", IconSize = 16 }, new Label { Text = ((Customer)item).Name } },
};
```

## Virtualization

A list box **virtualizes by default**: it makes containers only for the rows in sight and
a few around them, and reuses its own labels as rows scroll out and in. A list of ten
thousand customers has a couple of dozen labels, not ten thousand, measured on every
layout.

The rows must be of one height for this: `ItemHeight`, or — left `NaN` — the height of
the first row there is. A list of rows of different heights — cards, a feed — turns it off:

```csharp
var feed = new ListBox { IsVirtualizing = false, ItemTemplate = item => new PostCard((Post)item) };
```

With virtualization the children are no longer one per item, and a child's index in
`Children` is not its item's index. Find a row's container through the list:

| Member | |
| ------ | - |
| `ContainerFromIndex(index)` | The container of an item; null when it is out of sight |
| `IndexFromContainer(container)` | The item a container shows; −1 when it isn't one of this list's |
| `ScrollIntoView(index)` | Scrolls the item's row into sight; the container is made at the next layout |

`TreeView` and `VirtualizingStackPanel` virtualize with the same engine.

## Scrolling

The keys move the selection and bring the row they reach into sight: Home, End, the
arrows, and Shift with them in `Extended` mode. `ScrollPastEnd` lets a long list scroll on
until its last row is at the top — see [Layout](Layout#scrolling-past-the-end).

## Trees

`TreeView` shows `TreeNode`s: `Nodes` holds the top ones, each node its `Children`. A node
is a plain object the tree is built from — `Content` is the data, `ItemText` and
`ItemIcon` on the tree make its caption and icon. Since 0.20 a node raises
`PropertyChanged`, so a binding follows `IsExpanded`, `CheckedState` or `Content`, and one
to `node.Children` follows `Count`:

```csharp
caption.Bind(Label.TextProperty, node, n => n.IsExpanded ? "Open" : "Closed");
count.Bind(Label.TextProperty, node.Children, c => $"{c.Count} items");
```

### Check boxes

`CheckBoxes = true` puts a check box before every node; a click or Space checks it.
`CheckPropagation` says what that does to the rest of the tree:

| `CheckPropagation` | |
| ------------------ | - |
| `Cascade` (default) | The node takes its subtree along, and a parent shows its children: checked when all are, `Intermediate` when some are |
| `Indicate` | The children are left alone; a parent not checked itself shows `Intermediate` while something inside it is checked |
| `None` | Every node by itself — the tree before 0.20 |

`TreeNode.CheckedState` has the third state; `IsChecked` is true for `Checked` only. A click
on an intermediate node checks it. `NodeCheckedChanged` comes once per change, with the
node it started from and every node it changed (`ChangedNodes`) — checking a folder of ten
thousand files raises it once. Under `Cascade` the children a node makes in
`NodeExpanding` take its state: a checked folder read later has its files checked.

### Rows and the expander

A row is a `TreeViewItem`, styled like any control, and it takes its node's pseudo-classes:
`:expanded`, `:selected`, `:checked`, `:indeterminate`.

```css
TreeViewItem { ExpanderGlyph: Triangle; ExpanderWidth: 22; }
TreeViewItem:expanded { GlyphColor: Colors.DodgerBlue; }
TreeViewItem:indeterminate { IndeterminateGlyph: Square; }
```

| Property | |
| -------- | - |
| `ExpanderGlyph` | `Chevron` (default), `Triangle`, `PlusMinus`, `None` |
| `ExpanderWidth` | The strip of the expander and its click zone; 18 by default, 0 — none |
| `ExpanderGlyphSize` | The glyph across; 8 by default |
| `CollapsedIcon`, `ExpandedIcon` | Icons of one's own. `CollapsedIcon` alone turns a quarter as the node expands |
| `GlyphColor` | The expander and the empty check box |
| `IndeterminateGlyph` | The third state of the check box, as `CheckBox` has it |

The expander turns as the node expands, and at once with reduced motion. For anything else
a tree of its own overrides `CreateItem` and returns rows that override `DrawExpander`.
