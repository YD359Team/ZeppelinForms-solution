# Recipes

Short answers to "how do I…" — each one a task that crosses a few pages of this wiki,
with the code to start from. The pages linked from each recipe go deeper.

- [Close to the tray](#close-to-the-tray)
- [One action for a menu item, a button and a shortcut](#one-action-for-a-menu-item-a-button-and-a-shortcut)
- [Commands and a style sheet in markup](#commands-and-a-style-sheet-in-markup)
- [Ask before closing unsaved work](#ask-before-closing-unsaved-work)
- [A dialog that returns a value](#a-dialog-that-returns-a-value)
- [Load data without freezing the window](#load-data-without-freezing-the-window)
- [A clock in the status bar](#a-clock-in-the-status-bar)
- [Follow the system's light or dark mode](#follow-the-systems-light-or-dark-mode)
- [Edit styles while the application runs](#edit-styles-while-the-application-runs)
- [Report what the handlers throw](#report-what-the-handlers-throw)

## Close to the tray

The close button hides the window while an icon in the tray can bring it back; the icon's
menu quits for real.

```csharp
public class MainForm : Form
{
    private readonly TrayIcon _tray = new() { ToolTip = "Orders" };
    private bool _quitting;

    public MainForm()
    {
        var show = new MenuItem { Text = "Show" };
        show.Click += (_, _) => Show();

        var quit = new MenuItem { Text = "Quit" };
        quit.Click += (_, _) => { _quitting = true; Close(); };

        _tray.Menu.AddRange([show, MenuItem.Separator, quit]);
        _tray.Click += (_, _) => Show();
        _tray.Visible = true;

        Closing += (_, e) =>
        {
            // no tray on this desktop: closing closes
            if (_quitting || !_tray.IsShown || e.Reason != CloseReason.UserClosing) return;

            e.Cancel = true;
            Hide();
        };

        Closed += (_, _) => _tray.Dispose();
    }
}
```

The menu is the system's own, built from `Menu` each time it opens; `MenuOpening` comes
first, the place to update the items. Windows shows the icon in the notification area,
Linux on a panel that hosts StatusNotifierItems (KDE, XFCE, Cinnamon, LXQt, waybar, GNOME
with the AppIndicator extension) — `App.Capabilities` has `SystemTray` when there is one.
See [Forms and windows](Forms-and-Windows).

## One action for a menu item, a button and a shortcut

A `Command` runs the same way from each of them and is enabled or disabled in all of them
at once. Its `Gesture` is shown beside the menu item's caption and run by the form.

```csharp
var save = new Command(Save, () => _document.IsDirty)
{
    Gesture = KeyGesture.Parse("Ctrl+S"),
};

var file = new MenuItem { Text = "&File" };
file.Items.Add(new MenuItem { Text = "&Save", Command = save });

var toolbarSave = new Button { Text = "Save", Command = save };

// a shortcut that works with the menu closed — and without a menu at all
KeyBindings.Add(new KeyBinding(save));

// what CanExecute reads has changed: the button asks again
_document.Changed += (_, _) => save.NotifyCanExecuteChanged();
```

A menu asks `CanExecute` as it opens, a shortcut before it runs; only a button keeps
listening, which is what `NotifyCanExecuteChanged` is for. `ToggleCommand` adds a checked
state that a menu item's check mark and a `ToggleButton` share. Any `ICommand` works —
the `RelayCommand` of CommunityToolkit.Mvvm included.

## Commands and a style sheet in markup

A `.zfml` file names a command of its class by name, and carries styles of its own for
what it builds. Both are checked by the build.

```xml
<StackPanel x:Class="MyApp.EditorBar"
            xmlns="https://zeppelinforms.dev/markup"
            xmlns:x="https://zeppelinforms.dev/markup/x"
            Orientation="Horizontal" Spacing="6">

  <StackPanel.Styles>
    <Style Selector="Button.quiet">
      <Setter Property="Background" Value="transparent"/>
      <Style Selector="^:hover">
        <Setter Property="Background" Value="alpha(@Accent, 0.1)"/>
      </Style>
    </Style>
  </StackPanel.Styles>

  <Button Text="Save" Command="Save"/>
  <Button Text="Quit" Classes="quiet" Command="AppCommands.Quit"/>
</StackPanel>
```

```csharp
public partial class EditorBar
{
    public Command Save { get; }

    public EditorBar()
    {
        // before InitializeComponent: it hands the command to the button
        Save = new Command(DoSave);
        InitializeComponent();
    }
}
```

`Command="Save"` is a property or a field of the class; `Command="AppCommands.Quit"` a
static one of another type. Make the command before `InitializeComponent` runs — in the
constructor before the call, or in an initializer. A sheet in a file goes in with
`<StyleInclude Source="Styles/editor.zss"/>` among the styles. See
[Markup and tooling](Markup-and-Tooling).

## Ask before closing unsaved work

`Closing` can't wait for an answer, so it cancels first and closes again once the user
has agreed.

```csharp
private bool _discardConfirmed;

public EditorForm()
{
    Closing += async (_, e) =>
    {
        if (_discardConfirmed || !_document.IsDirty) return;

        // decided before the first await: the window stays open
        e.Cancel = true;

        if (await MessageBox.ConfirmAsync(this, "Close without saving your changes?"))
        {
            _discardConfirmed = true;
            Close();
        }
    };
}
```

The async message box works everywhere, the browser included. See
[Forms and windows](Forms-and-Windows).

## A dialog that returns a value

Any form is a dialog: `ShowDialogAsync<T>` waits until it closes with `Accept(value)` or
`Cancel()`.

```csharp
public sealed class RenameDialog : Form
{
    public RenameDialog(string name)
    {
        Title = "Rename";
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var box = new TextBox { Text = name, Size = new(260, float.NaN) };

        var ok = new PrimaryButton { Text = "OK" };
        ok.Click += (_, _) => Accept(box.Text);

        var cancel = new Button { Text = "Cancel" };
        cancel.Click += (_, _) => Cancel();

        Content = new StackPanel { Padding = new(16), Spacing = 12, Children = { box, ok, cancel } };
    }
}

DialogResult<string> result = await new RenameDialog(file.Name).ShowDialogAsync<string>(this);

if (result.TryGetValue(out string newName))
    file.Rename(newName);
```

## Load data without freezing the window

The work goes to a worker; the `await` comes back to the UI thread, where the controls
may be touched.

```csharp
Shown += async (_, _) =>
{
    status.Text = "Loading…";

    List<Order> orders = await Task.Run(() => _repository.LoadOrders());   // on a worker

    grid.DataSource = orders;                                              // on the UI thread
    status.Text = $"{orders.Count} orders";
};
```

From a thread of its own, code reaches the UI through `form.BeginInvoke(…)`. See
[Threading and timers](Threading-and-Timers).

## A clock in the status bar

`DispatcherTimer` ticks on the UI thread; made for a form, it stops when the form closes.

```csharp
var clock = new DispatcherTimer(this) { Interval = TimeSpan.FromSeconds(1) };
clock.Tick += (_, _) => status.Text = DateTime.Now.ToLongTimeString();
clock.Start();
```

## Follow the system's light or dark mode

```csharp
App.UseSystemTheme(Themes.FluentLight, Themes.FluentDark);

App app = new(LinuxPlatform.Create()) { MainForm = new MainForm() };
app.Run();
```

The theme follows the system's mode and accent as they change — Windows, the browser,
Android, and Linux through the desktop portal. Pass `followAccent: false` to keep a brand
color. See [Styles and themes](Styles-and-Themes).

## Edit styles while the application runs

```csharp
form.Styles.Load("Assets/app.zss", watch: true);
```

Every save of the sheet — or of a sheet it `@import`s — restyles the open forms in the
next frame, transitions included. Problems go to the debug output with their file and
line. Watching is for development: in the browser and on Android the sheet is read once.

## Report what the handlers throw

```csharp
App.UnhandledException += (_, e) =>
{
    log.Error(e.Exception);

    // the application goes on; without this the exception ends it
    e.Handled = true;
};
```

An event handler, a timer's tick, a frame — each runs on its own, and what it throws is
offered here before it would end the application. See
[Threading and timers](Threading-and-Timers).