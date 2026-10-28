# ZeppelinForms

**ZeppelinForms** (ZF) is a code-first, cross-platform UI framework for .NET 10: the
simplicity of WinForms, a few well-chosen ideas from WPF, Avalonia and Flutter, and none
of the legacy baggage. One UI runs on Windows, Linux (X11), in the browser (WebAssembly)
and on Android, drawn with SkiaSharp.

```csharp
App app = new(new WindowsPlatform()) { MainForm = new MainForm() };
app.Run();
```

```csharp
public class MainForm : Form
{
    public MainForm()
    {
        Title = "Hello";
        Size = new Size(640, 480);

        var name = new TextBox { Watermark = "Your name", Docking = Dock.Top };
        var greet = new PrimaryButton { Text = "Greet", Docking = Dock.Top };
        greet.Click += async (_, _) => await MessageBox.ShowAsync(this, $"Hello, {name.Text}!");

        Content = new DockPanel { Padding = new(16), Children = { name, greet, new Panel() } };
    }
}
```

The showcase — every control, the styles and the effects — runs in the browser:
**https://yd359team.github.io/**

## Contents

**Start**
- [Getting started](Getting-Started) — packages, the application per platform, the `zf-app` template, the first form
- [Forms and windows](Forms-and-Windows) — size, position, limits, dialogs, message boxes
- [Recipes](Recipes) — close to the tray, commands, confirmations, dialogs, loading, live styles

**Building the UI**
- [Layout](Layout) — measure and arrange, sizes, alignment, panels, docking, scrolling
- [Controls](Controls) — what there is, and how to write your own
- [Lists and virtualization](Lists-and-Virtualization) — list boxes over long data
- [Data grid](DataGridView) — typed columns, editing, selection, sorting
- [Data binding](Data-Binding) — bindings, data sources, `DataSource<T>`
- [Mobile and navigation](Mobile-and-Navigation) — a stack of pages, back, app and navigation bars, sheets, swipes, snackbars, the safe area

**Looks**
- [Styles and themes](Styles-and-Themes) — style sheets, themes, styled properties
- [Graphics and effects](Graphics-and-Effects) — drawing, effects, blend modes, fonts

**Behaviour**
- [Threading and timers](Threading-and-Timers) — the UI thread, `DispatcherTimer`, unhandled exceptions
- [Accessibility](Accessibility) — screen readers, keyboard, high contrast, text scale

**Tooling**
- [Markup, previewer and Hot Reload](Markup-and-Tooling)
- [The browser](Browser) — WebAssembly specifics, fonts, preloading
- [Testing](Testing) — the headless platform, snapshots
- [Public API and releases](Public-API-and-Releases) — Shipped/Unshipped, versioning

The [changelog](https://github.com/YD359Team/ZeppelinForms-solution/blob/master/CHANGELOG.md)
lists what changed in every release.
