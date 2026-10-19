# Markup, previewer and Hot Reload

## Markup

Code-first is the main way to build a UI; markup is an option for the parts that read better
as a tree. A `.zfml` file names its class with `x:Class`; at build time a source generator
turns it into C# — a field for every `x:Name` and an `InitializeComponent()` of plain `new`,
assignments, `+=` and `Add`. No reflection, so trimming and native AOT keep it all.

```xml
<StackPanel x:Class="MyApp.SignUpCard"
            xmlns="https://zeppelinforms.dev/markup"
            xmlns:x="https://zeppelinforms.dev/markup/x"
            xmlns:my="using:MyApp.Controls"
            Orientation="Vertical" Spacing="10">
  <TextBox x:Name="NameBox" Watermark="Your name" Size="260,NaN"/>
  <Label Text="{Bind NameBox.Text}"/>
  <PrimaryButton Text="Sign up" Classes="wide" Click="OnSubmit"/>
  <my:Gauge Level="0.5"/>
</StackPanel>
```

```csharp
public partial class SignUpCard
{
    private void OnSubmit(object? sender, MouseClickEventArgs e) { /* NameBox.Text … */ }
}
```

- Values are written as in style sheets: `Padding="14 6"`, `Size="260,NaN"`,
  `Background="#0d6efd"`, enum members, static members by name.
- Children go to the first property that takes them — one marked `[Content]`, then
  `Children`, `Items`, `Nodes`, `Content`, `Child`; `<Border.Child>` names one.
- `{Bind Name.Property}` binds a styled property to a named element, `Mode=TwoWay` both
  ways; `{x:Null}` is null; `<x:String>` is a text item of a list.
- An unknown type, property, event or value is a ZF2xxx error at its line of the markup, and
  so is a handler of the wrong signature.
- A class whose code declares no constructor gets one that calls `InitializeComponent`.

The package's build picks `.zfml` files up by itself.

## Analyzers

The `ZeppelinForms` package brings analyzers that catch the framework's own pitfalls at build
time — ZF0xxx for code (an assignment in a constructor that locks the theme out, a styled
property declared wrong…) and ZF2xxx for markup.

## Previewer

The **ZeppelinForms Tools** extension for Visual Studio 2022 (17.10 and later) and Visual
Studio 2026 — the `.vsix` is attached to every release.

Ctrl+Alt+Shift+P (View › Other Windows › ZeppelinForms Preview) previews the active project:
every form with a parameterless constructor and every method marked `[Preview]` (namespace
`ZeppelinForms.Design`). What `Main` does before showing a form — loading style sheets,
choosing a theme — goes into a `[PreviewSetup]` method. The extension also highlights `.zss`
style sheets and lists their problems in the Error List.

## Hot Reload

Visual Studio's Hot Reload and `dotnet watch` work with code-first forms. After every applied
edit the open forms restyle, measure and redraw everything, so an edited `Draw`,
`MeasureOverride` or theme rule shows at once; pages made by factories — the entries of a
`SplitView`, the pages of a `PageControl` — are made again when their assembly changed. A
form built in its constructor builds itself again in `OnHotReload`:

```csharp
public class MainForm : Form
{
    public MainForm()
    {
        Title = "Orders";
        Content = CreateContent();
    }

    private UIElement CreateContent() => new StackPanel { /* … */ };

    protected override void OnHotReload(HotReloadEventArgs e) => Content = CreateContent();
}
```

`UIElement.OnHotReload` does the same for a control that builds its children itself, and
`HotReload.Applied` is raised once the forms have caught up. Style sheets loaded with
`watch: true` follow their saves by themselves, and markup is built again after an edit.

## The inspector

F12 or Ctrl+Shift+I over a running form: the element under the cursor with its margin and
padding, its properties in a property grid (editable), and the [accessibility](Accessibility)
audit. It opens as an overlay or docked to the side.
