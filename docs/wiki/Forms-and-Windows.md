# Forms and windows

A **form** is the only kind of window, as in WinForms. It is not an element itself: its
`Content` is, and everything inside the window hangs from it.

```csharp
public class OrdersForm : Form
{
    public OrdersForm()
    {
        Title = "Orders";
        Size = new Size(1024, 768);
        MinimumSize = new Size(640, 480);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        Content = CreateContent();
    }
}
```

## Lifecycle

| Event | When |
| ----- | ---- |
| `Shown` | The window is on the screen for the first time — a good place to start loading data |
| `Closing` | The window is about to close; `e.Cancel = true` keeps it open |
| `Closed` | The window is gone |

## Size and position

| Property | Units | Notes |
| -------- | ----- | ----- |
| `Size` | logical units | The window's size; with `SizeToContent` the client area on every platform |
| `ClientSize` | logical units | The area inside the frame |
| `Position` | **screen pixels** | The top-left corner, in the units `Displays` reports screens in |
| `MinimumSize`, `MaximumSize` | logical units | A `NaN` axis has no limit |

`Position` is in screen pixels because with screens of different scales a logical point
of the desktop has no single meaning. On Linux it is the corner of the client area: the
frame belongs to the window manager.

Both `Size` and `Position` work both ways while the window is open: setting them moves or
resizes the window, and a move or resize by the user updates them. `LocationChanged` and
`SizeChanged` tell about either; `ClientSizeChanged` follows the area inside the frame.

```csharp
form.SizeChanged += (_, _) => settings.WindowSize = form.Size;
form.LocationChanged += (_, _) => settings.WindowPosition = form.Position;
```

The limits keep the user's dragging inside them, and an open window outside new limits
is brought inside. Setting a minimum above the maximum raises the maximum, and the other
way round, as in WinForms.

| Member | Does |
| ------ | ---- |
| `TopMost` | Keeps the window above every window that isn't topmost — a tool palette, a timer |
| `CenterToScreen()` | Centers the window in the working area of its screen (the taskbar left out) |
| `WindowState` | Normal, minimized or maximized |
| `WindowStartupLocation` | `Manual` (uses `Position`), `CenterScreen`, `CenterOwner` |

The browser and Android have no windows in this sense: these members are accepted and
do nothing there.

## Sized to the content

`SizeToContent` makes the window as large as what it shows — a dialog whose text and
buttons decide its size. `MaximumSize.Width` is then also where long text wraps.

```csharp
var dialog = new Form
{
    Title = "About",
    SizeToContent = SizeToContent.WidthAndHeight,
    MaximumSize = new Size(480, float.NaN),
    Content = aboutPanel,
};
```

## Fonts

`Form.Font` is the font of every element of the form that has no font of its own and
inherits none. It is never null: until set it is `Font.Default`, read live, so a theme
switch reaches a form that set no font. Assigning `null` resets it.

## Dialogs

A form can be shown as a modal dialog over an owner. The dialog closes itself with a
result through `Accept(value)`.

```csharp
DialogResult<string> result = await new RenameForm(name).ShowDialogAsync<string>(this);

if (result.IsAccepted)
    Rename(result.Value);
```

`ShowDialog<T>` blocks the calling code in a nested loop on the desktop; the browser and
Android have none, so use `ShowDialogAsync` there — and everywhere, if the code is shared.

### Message boxes and input boxes

```csharp
MessageBoxResult answer = await MessageBox.ShowAsync(
    this, "Save the changes?", "Orders", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

bool sure = await MessageBox.ConfirmAsync(this, "Delete the order?");
await MessageBox.ErrorAsync(this, "The server did not answer.");

string? name = await InputBox.ShowAsync(this, "What is your name?", initialValue: "Zeppelin");
decimal? amount = await InputBox.ShowNumberAsync(this, "Amount");
```

The boxes are sized to their message and buttons, take the theme's colors and follow
the interface language.

### Overlays, flyouts and toasts

| Member | Shows |
| ------ | ----- |
| `ShowFlyout(anchor, content, placement)` | A popup next to an element, closed by a click outside or Escape |
| `AddOverlay(content)` / `RemoveOverlay` | An element over the content that stays until removed |
| `ShowToast(message)` | A short notice that goes away on its own |
| `ShowContextMenu(items, position)` | A context menu |

A flyout that doesn't fit on its side opens on the opposite one and stays inside the
window.

## Invoking on the window's thread

The window belongs to the UI thread. Work from another thread reaches it through
`form.Invoke(…)`, `form.BeginInvoke(…)` or the form's `Dispatcher` — see
[Threading and timers](Threading-and-Timers).

## The inspector

F12 (or Ctrl+Shift+I) opens the inspector over any form: the element under the cursor
with its margin and padding, its properties in a property grid, and the accessibility
audit.
