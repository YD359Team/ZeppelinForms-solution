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

## The whole application from a template

```
dotnet new install ZeppelinForms.Templates
dotnet new zf-app -n Notes
```

makes that layout:

| Project | What it is |
| ------- | ---------- |
| `Notes` | the application: `MainForm` — an app bar, a stack of pages, a navigation bar — and its pages |
| `Notes.Desktop` | Windows and Linux from one build; `Program` picks the platform when it starts |
| `Notes.Browser` | WebAssembly: `wwwroot` with `index.html`, `main.js`, `zf.js` and a font |
| `Notes.Android` | the activity |

`--desktop false`, `--browser false`, `--android false` leave a project out. The browser
project needs the `wasm-tools` workload, the Android one the `android` workload
(`dotnet workload install …`). The version of the ZeppelinForms packages is
`ZeppelinFormsVersion` in `Directory.Build.props` — the templates' own version unless
`--zfVersion` says otherwise. The pages and the frame are described in
[Mobile and navigation](Mobile-and-Navigation).

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

The same form in markup — `MainForm.zfml`, with nothing to declare before the root:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Form Class="MyApp.MainForm" Title="Hello" Size="480,320" WindowStartupLocation="CenterScreen">
  <StackPanel Orientation="Vertical" Spacing="10" Padding="24">
    <Label Text="What is your name?"/>
    <TextBox Name="NameBox" Watermark="Your name"/>
    <PrimaryButton Text="Greet" Click="OnGreet"/>
  </StackPanel>
</Form>
```

and its behaviour in `MainForm.cs`:

```csharp
public partial class MainForm
{
    private async void OnGreet(object? sender, MouseClickEventArgs e) =>
        await MessageBox.ShowAsync(this, $"Hello, {NameBox.Text}!");
}
```

The generator writes the rest: the base class, a field for `NameBox` and a constructor
that builds the markup. See [Markup](Markup-and-Tooling#a-form-in-markup).

Or in code, but with the controls apart from what they do — `MainForm.design.cs` and
`MainForm.cs`, as WinForms has them: see [Design apart from code](Markup-and-Tooling#design-apart-from-code).

### Templates

```
dotnet new install ZeppelinForms.Templates

dotnet new zf-app -n Notes                 # an application: shared, Desktop, Browser, Android
dotnet new zf-form -n OrderForm            # OrderForm.cs + OrderForm.design.cs
dotnet new zf-form-markup -n FeedbackForm  # FeedbackForm.cs + FeedbackForm.zfml
dotnet new zf-stylesheet -n app            # app.zss
```

Run the item templates in the project's folder: the namespace is the project's root namespace, or
`--namespace`. Rider lists the templates under Add › New from Template; recent versions of
Visual Studio show the templates `dotnet new` installed in Add › New Item.

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
