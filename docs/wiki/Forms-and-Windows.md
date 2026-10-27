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

## The taskbar button

`form.Taskbar` is the form's button on the taskbar or the dock: the progress of a long work
and a count, seen without opening the window. `form.RequestAttention()` asks for the user
without taking the focus — the button flashes until the window is activated.

```csharp
form.Taskbar.ProgressValue = done / (double)total;          // 0..1, turns the bar on
form.Taskbar.ProgressState = TaskbarProgressState.Paused;   // Indeterminate, Normal, Paused, Error
form.Taskbar.Badge = unread;                                 // null or 0 — none

form.Taskbar.Clear();                                        // a plain button again
form.RequestAttention();                                     // a background window only
```

| | Windows | Linux (X11, Wayland) | Browser |
| --- | --- | --- | --- |
| Progress | the taskbar button | the launcher entry | — |
| Badge | an overlay in the button's corner | the launcher entry's count | an installed web app's badge |
| Attention | the button flashes | the window is marked | the tab's title blinks |

On Linux the progress and the badge go to the dock as the launcher entry
(`com.canonical.Unity.LauncherEntry`) of `application://{App.Id}.desktop` — KDE's task
manager, Dash to Dock and Plank show it, GNOME's own dash doesn't. It is the application's:
every form shows on the one entry, and `App.Id` has to be the name of its .desktop file.
`PlatformCapabilities.TaskbarProgress`, `TaskbarBadge` and `WindowAttention` tell what the
platform can. Values set before the form is shown come with its window.

## Notifications

`Notifications.Show` shows the system's notification — outside the windows, in the corner
of the screen and in the notification center. `Clicked` comes on the UI thread; `Activate`
brings the form forward from there.

```csharp
var done = new Notification("Export finished", "orders.csv — 1 248 rows")
{
    Kind = NotificationKind.Information,     // None, Information, Warning, Error
};

done.Clicked += (_, _) => form.Activate();
Notifications.Show(done);

// or in one line; null — the platform has none
Notifications.Show("Sync failed", "The server didn't answer", NotificationKind.Error);

// from a tray icon: its balloon on Windows, as NotifyIcon.ShowBalloonTip
tray.ShowNotification("Synced", "3 files");
```

| Platform | Shown as |
| --- | --- |
| Windows | a balloon of a tray icon, which Windows 10 and 11 show as a toast. A visible `TrayIcon` of the application carries it; without one the application's icon is put into the tray for as long as the notification lasts |
| Linux, X11 and Wayland | `org.freedesktop.Notifications` on the session bus — GNOME, KDE, XFCE, mako, dunst. The `desktop-entry` hint is `App.Id`, so the application's .desktop file gives it its name and icon; an error stays until dismissed |
| Browser | the Notification API; the first notification asks the user's permission, and a refusal closes it |

`Closed` comes once: clicked, dismissed, timed out, or replaced by a newer balloon of the
same tray icon. `PlatformCapabilities.Notifications` tells whether there is a way; Android
has none yet.

`form.Activate()` brings a window forward and gives it the keyboard — a hidden form is
shown again first. The systems let an application do it after the user's own input, such
as a click on its notification or tray icon; otherwise the button flashes instead.

## The application's identity

`App.Id`, `App.DisplayName` and `App.Version` say who the application is. Each is read from
the entry assembly until set — `App.Version` is its `<Version>`, without the "+commit" the SDK
appends — so a version kept in one place, the project file or Directory.Build.props, is
there without writing it again:

```csharp
App.Id = "com.example.Orders";              // before Run: the windows are made with it
new App(new WindowsPlatform()) { MainForm = new MainForm() }.Run();

aboutLabel.Text = $"{App.DisplayName} {App.Version}";
```

The id groups the windows on the taskbar and finds the application's .desktop file: it is
the window class (WM_CLASS) on X11, the app_id on Wayland and the application user model id
on Windows. Windows gets it only when it is set — a running window with an id no longer
groups with a button pinned from a shortcut made without one.

## Invoking on the window's thread

The window belongs to the UI thread. Work from another thread reaches it through
`form.Invoke(…)`, `form.BeginInvoke(…)` or the form's `Dispatcher` — see
[Threading and timers](Threading-and-Timers).

## The inspector

F12 (or Ctrl+Shift+I) opens the inspector over any form: the element under the cursor
with its margin and padding, its properties in a property grid, and the accessibility
audit.
