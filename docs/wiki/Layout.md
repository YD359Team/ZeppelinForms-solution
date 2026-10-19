# Layout

Layout runs in two passes, as in WPF and Avalonia: **Measure** asks every element how
large it wants to be within the room it is offered, **Arrange** gives it its place.
Panels do both for their children; a control of your own overrides `MeasureOverride` and
`ArrangeOverride` (or the `*ContentOverride` pair of a `DecoratedPanel`).

## Size

An element's size is its content's, unless it is given one.

| Member | Sets |
| ------ | ---- |
| `Size = new Size(200, 30)` | Both axes |
| `Width = 200` | One axis, the other left to the layout |
| `Height = 30` | One axis |
| `Size = Size.FromWidth(200)` | The same as `new Size(200, float.NaN)` |

`NaN` on an axis means *not set*: the layout decides it. An explicit size wins over
stretching — a progress bar of `Size(100, 10)` in a wider slot keeps its width and stands
in the middle of the slot.

## Alignment, margin, padding

`HorizontalAlignment` and `VerticalAlignment` place an element inside the slot its parent
gives it: `Left`/`Top`, `Center`, `Right`/`Bottom` or `Stretch`. Controls (buttons, text
boxes) default to `Center`, containers to `Stretch`.

```csharp
var editor = new TextBox
{
    IsMultiline = true,
    Height = 240,
    HorizontalAlignment = HorizontalAlignment.Stretch,   // as wide as its slot
};
```

`Margin` is the room around an element, `Padding` the room inside it, between its
border and its content.

## Panels

| Panel | Lays out |
| ----- | -------- |
| `StackPanel` | One after another, vertically or horizontally, with `Spacing`. A child with `FlexGrow` shares the free room |
| `WrapPanel` | In rows that wrap, with `Spacing` and `LineSpacing` |
| `Grid` | In rows and columns: `Rows = "Auto,*,2*"`, `Columns = "200,*"` |
| `DockPanel` | Each child to an edge, the last one into what is left |
| `UniformGrid` | In equal cells |
| `Panel` | On top of each other, each by its alignment |

### StackPanel

```csharp
var form = new StackPanel
{
    Orientation = Orientation.Vertical,
    Spacing = 10,
    CrossAxisAlignment = CrossAxisAlignment.Stretch,   // every child as wide as the stack
    Children = { title, nameBox, emailBox, submit },
};
```

`CrossAxisAlignment` decides the slot across the stack; each child then aligns itself
inside its slot. A stack asks for what its children need and no more — unless a child has
`FlexGrow`, which takes the free room along the stack.

### Grid

```csharp
var grid = new Grid { Columns = "Auto,*", Rows = "Auto,Auto,*" };

grid.Children.Add(new Label { Text = "Name" }, 0, 0);    // row, column
grid.Children.Add(nameBox, 0, 1);
grid.Children.Add(notes, 2, 0);
notes.ColumnSpan = 2;
```

Track sizes are fixed (`200`), `Auto` (by the largest child) or stars (`*`, `2*`) — shares
of what is left. A grid without `Rows` or `Columns` is one star cell on that axis, so its
children are laid over each other, each by its alignment.

### DockPanel

```csharp
var root = new DockPanel
{
    Children =
    {
        new MenuBar { Docking = Dock.Top },
        new Label { Text = "Ready", Docking = Dock.Bottom },
        new TreeView { Docking = Dock.Left, Width = 240 },
        editor,                                   // the last child fills the rest
    },
};
```

Children are docked in their order, each to what the previous ones left. The last child
fills the rest unless `LastChildFill` is turned off, or it is docked to an edge of its own.

A child docked to the top or the bottom spans the whole width **even over an explicit
width**, as in WinForms: a text box of `Size(200, 30)` docked to the top runs across the
panel. Docked left or right, it spans the height. Give it `Height` alone to leave the width
to the dock. An alignment the application chose along the edge is kept: a row of buttons
docked to the bottom with `HorizontalAlignment.Right` stays at the right.

`Docking = Dock.Fill` outside a `DockPanel` means *fill the slot*, whatever the alignment.

## Scrolling

Every panel scrolls its content when `OverflowX` / `OverflowY` is `Auto` (a bar when the
content doesn't fit) or `Scroll` (always a bar); `Hidden` clips, `Visible` neither clips nor
scrolls. Panels scroll with the wheel, the bars and — on touch screens — the finger, with kinetic flings and rubber-band overscroll.

```csharp
var page = new StackPanel { OverflowY = Overflow.Auto, ScrollBarMode = ScrollBarMode.Inline };
page.ScrollTo(0, 0);
```

`ScrollBarMode.Overlay` (the default) draws the bars over the content; `Inline` takes
room for them.

### Scrolling past the end

`ScrollPastEnd = true` lets a panel, a list, a tree, a data grid or a multi-line text box
scroll on past its content until the last row stands at the top — the way a code editor
scrolls past its last line. Every way of scrolling follows it: the wheel, the bars, a
finger and `ScrollTo`. It is off by default: in a form a list that scrolls into emptiness
looks broken; an editor, a log or a long document is where it belongs.

```csharp
var log = new TextBox { IsMultiline = true, ScrollPastEnd = true };
```

## Reacting to the size

A control of your own overrides `OnSizeChanged()`: it is called when an arrange gave the
element another size, not on every layout pass. A form has `SizeChanged` and
`ClientSizeChanged`. To choose content by the room available, `AdaptiveLayout` and
`LayoutBuilder` do it for you.
