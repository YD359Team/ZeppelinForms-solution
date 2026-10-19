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
