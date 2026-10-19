# Getting started

## Packages

The UI references the `ZeppelinForms` package. The application references the package
of its platform as well, which brings the framework with it:

| Platform    | Package                 | Started by                                         |
| ----------- | ----------------------- | -------------------------------------------------- |
| Windows     | `ZeppelinForms.Windows` | `new App(new WindowsPlatform())` (`ZeppelinForms.Windows`) |
| Linux (X11) | `ZeppelinForms.Linux`   | `new App(new X11Platform())` (`ZeppelinForms.Linux`) |
| Browser     | `ZeppelinForms.Browser` | `BrowserApp.RunAsync(…)`                           |
| Android     | `ZeppelinForms.Android` | `AndroidApp.Run(activity, …)`                      |

A common layout for a cross-platform application is a class library with the forms
(`ZeppelinForms`) and a small executable per platform that only starts it — the way
[ZF Process Monitor](https://github.com/YD359Team/ZF_ProcessMonitor) is built.

## Windows

A `WinExe` project for `net10.0-windows`:

```csharp
using ZeppelinForms;
using ZeppelinForms.Theming;
using ZeppelinForms.Windows;

public static class Program
{
    [STAThread]   // drag and drop from other applications needs a single-threaded apartment
    static void Main()
    {
        App app = new(new WindowsPlatform())
        {
            MainForm = new MainForm(),
            StartupTheme = Themes.FluentLight,   // optional
        };

        app.Run();
    }
}
```

## Linux (X11)

An `Exe` project for `net10.0`:

```csharp
App app = new(new X11Platform()) { MainForm = new MainForm() };
app.Run();
```

## Browser (WebAssembly)

```csharp
public static class Program
{
    static Task Main() => BrowserApp.RunAsync(
        () => new MainForm(),
        font: "/fonts/Inter-Regular.ttf",
        preload: ["/Assets/Logo.png"]);
}
```

A factory is passed rather than a form: a form measures text in its constructor, and
the text measurer exists only once the platform is made. See [The browser](Browser)
for the host files, fonts and what differs there.

## Android

From the activity's `OnCreate`:

```csharp
AndroidApp.Run(this, () => new MainForm(), assets: ["logo.png"]);
```

The files named in `assets` are copied out of the APK so that `Assets.Root` finds them.

## The first form

A form is a window; its `Content` is any element. Code-first is the main way to build
it — [markup](Markup-and-Tooling) is an option for the parts that read better as a tree.

```csharp
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Dialogs;
using ZeppelinForms.Forms.Enums;

public class MainForm : Form
{
    private readonly TextBox _name = new() { Watermark = "Your name" };

    public MainForm()
    {
        Title = "Hello";
        Size = new Size(480, 320);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var greet = new PrimaryButton { Text = "Greet" };
        greet.Click += async (_, _) => await MessageBox.ShowAsync(this, $"Hello, {_name.Text}!");

        Content = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 10,
            Padding = new(24),
            Children = { new Label { Text = "What is your name?" }, _name, greet },
        };
    }
}
```

## Files and assets

| File | What it is | How it is used |
| ---- | ---------- | -------------- |
| `.zss` | Style sheet | `App.Styles.Load(path, watch: true)`, `StyleSheet.Parse(text)` |
| `.zfml` | Markup, compiled into C# at build time | picked up by the package's build |
| `.ico` | Window and application icon | `Icon.FromFile(path)` |
| `.png`, `.jpg`, `.webp`, `.gif`, `.bmp` | Images | `Image.LoadAsset("logo.png")` |
| `.ttf`, `.otf` | Fonts | `Font.Default.WithFile(path)`, `FontFallbacks.Add(path)` |

Assets are looked up in `Assets.Root`, the `Assets` folder next to the executable. The
project copies them there:

```xml
<ItemGroup>
  <None Include="Assets\**\*" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

## Where to go next

- [Layout](Layout) — how elements get their size and place
- [Data grid](DataGridView) and [data binding](Data-Binding) — showing and editing records
- [Styles and themes](Styles-and-Themes) — the look, without touching the code
