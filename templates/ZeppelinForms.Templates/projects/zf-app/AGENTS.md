# AGENTS.md

Instructions for AI coding agents working on MyApp: Claude Code, Codex, Copilot, Cursor
and the like.

MyApp is a [ZeppelinForms](https://github.com/YD359Team/ZeppelinForms-solution) (ZF)
application. ZF is a code-first UI framework for .NET 10. **It is not WinForms, WPF, Avalonia
or MAUI**, though it borrows names from them.

- **Don't guess an API** from those frameworks. Check what ZF has: the
  [wiki](https://github.com/YD359Team/ZeppelinForms-solution/wiki), IntelliSense, or the
  package's XML docs.
- When a member doesn't exist, say so rather than inventing one.

## The projects

| Project | What it is | Run it |
| ------- | ---------- | ------ |
| `MyApp` | The application: `MainForm`, its pages in `Pages/`, `Startup.Configure()` | — |
| `MyApp.Desktop` | Windows and Linux | `dotnet run --project MyApp.Desktop` |
| `MyApp.Browser` | WebAssembly; needs `dotnet workload install wasm-tools` | `dotnet run --project MyApp.Browser` |
| `MyApp.Android` | Android; needs `dotnet workload install android` | `dotnet build MyApp.Android -t:Run` |

The UI and its logic go into `MyApp`. The platform projects only start it: put code there
only when it is truly about one platform. The version of the ZeppelinForms packages is
`ZeppelinFormsVersion` in `Directory.Build.props`.

Build with `dotnet build MyApp.Desktop` while working: it is the quickest of the three to
build. The browser and Android projects build only with their workloads installed.

## Writing the UI

The UI is built in code, as objects with initializers:

```csharp
var name = new TextBox { Watermark = "Your name" };
var greet = new Button { Text = "Greet" };
greet.Click += async (_, _) => await MessageBox.ShowAsync(this, $"Hello, {name.Text}!");

Content = new StackPanel
{
    Orientation = Orientation.Vertical,
    Spacing = 8,
    Padding = new Thickness(16),
    Children = { name, greet },
};
```

- **The form**: a `Form` is the window. Its one `Content` is any element, usually a panel.
  `MainForm` here is a `Scaffold` with an app bar, a `NavigationHost` (a stack of `Page`s)
  and a navigation bar.
- **Navigation**: pages go on the stack with `NavigationHost.PushAsync(page)` and come off
  with `PopAsync()`. A page reached by a route goes there with `GoToAsync("details?id=3")`;
  routes are registered with `RegisterRoute` in `MainForm`. In the browser, the address
  follows the stack.
- **Layout**:
  - Panels: `StackPanel`, `Grid` (`Columns = "*,Auto,2*"`), `DockPanel` with `Docking`,
    `WrapPanel`.
  - Elements place themselves with `HorizontalAlignment`, `VerticalAlignment`, `Margin` and
    `Padding`. `Size` is `float.NaN` on an axis left to the layout.
  - A panel scrolls with `OverflowY = Overflow.Auto`.
- **Dialogs are async-only**: use `await form.ShowDialogAsync<T>(owner)`,
  `MessageBox.ShowAsync` and `InputBox.ShowAsync`. The browser and Android have no blocking
  dialogs, and this code runs on all three.
- **Threads**: touch elements only on the UI thread. From another thread, use
  `form.BeginInvoke(() => …)` or `await form.Dispatcher.InvokeAsync(…)`; a
  `DispatcherTimer` ticks on the UI thread already.
- **Data**:
  - `element.Bind(Label.TextProperty, (MyViewModel vm) => vm.Status)` reads from the
    `DataContext`; add a setter to make it two-way.
  - A list the UI follows is an `ObservableCollection<T>`. Change its items; don't assign a
    new collection (ZF0001).
- **Commands**: `new Command(Save, () => CanSave)` is shared by buttons, menu items and key
  gestures.
- **Looks**:
  - Colors and sizes come from the theme, set in `Startup.Configure()`.
  - Restyle with `.zss` style sheets (`App.Styles.Load("Styles/app.zss")`) and with
    classes: `button.Classes.Add("accent")`.
  - Hard-coded colors on elements override the theme, light or dark: use them sparingly.
- **Markup is optional**: a form can also be written in `.zfml` (`dotnet new zf-form-markup`)
  or with its design in a `.design.cs` (`dotnet new zf-form`).

## Writing a control of your own

- **Base class**: derive from `UnitControl` (no children), `PanelControl` (children) or
  `WrapControl` (one child). Use the `Decorated*` variants for a background and a border,
  and override `DrawContent`, since `Draw` is sealed there.
- **Values a theme or a style may set** are styled properties:
  `[Styled] public partial Color HoverColor { get; set; }` in a `partial` class.
  - In a constructor, set them with `SetControlDefault(HoverColorProperty, value)`. A plain
    assignment would lock the theme out (ZF0006).
  - After a change, call `Invalidate()` for layout and `InvalidateVisual()` for a repaint.
- **Subscriptions**: subscribe to anything outside the control in `OnAttached` and
  unsubscribe in `OnDetached`.

## Testing

A form runs without a screen on `HeadlessPlatform`. Add the `ZeppelinForms` package to an
xUnit project, then:

```csharp
var platform = new HeadlessPlatform();
var form = new MainForm { Size = new Size(420, 760) };
platform.CreateWindow(form);
form.UpdateLayout();

HeadlessInput.Click(form, x, y);   // positions from element.GetAbsolutePosition()
platform.PumpAll();                // run what was queued to the dispatcher
```

Run tests that open forms in one collection, not in parallel. See the wiki's
[Testing](https://github.com/YD359Team/ZeppelinForms-solution/wiki/Testing).

## Working here

- Keep the code building for all three platforms: anything only one platform has goes into
  that platform's project.
- Keep the existing comments. Change one only when what it says is no longer true.
- Don't commit or push unless you are asked to.
