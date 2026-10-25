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
- `Command="Save"` takes a command of the class — a property or a field — and
  `Command="AppCommands.Save"` a static one of another type, found in the class's
  namespace, in a `using:` namespace of the markup or by its full name.
- An unknown type, property, event, command or value is a ZF2xxx error at its line of the
  markup, and so is a handler of the wrong signature.
- A class whose code declares no constructor gets one that calls `InitializeComponent`.

The package's build picks `.zfml` files up by itself.

### Commands

A command property — `Command` of a button or a menu item — names a command instead of
taking a value written in the markup:

```xml
<Button Text="Save" Command="Save"/>
<Button Text="Quit" Command="AppCommands.Quit"/>
```

```csharp
public partial class EditorBar
{
    public Command Save { get; }

    public EditorBar()
    {
        Save = new Command(DoSave, () => _document.IsDirty);
        InitializeComponent();
    }
}
```

`InitializeComponent` reads the command when it builds the button: make it before the call
— in the constructor or an initializer. A name the markup can't find, a member that isn't a
command, or another type's member that isn't static is ZF2011.

### Style sheets

`<x:Styles>` holds a style sheet for the element it is in and everything inside it — the
element's own `Styles`, as if the code had added it there:

```xml
<StackPanel x:Class="MyApp.SignUpCard" …>
  <x:Styles><![CDATA[
    $gap: 8;

    #Result { TextColor: @Accent; }

    Button.quiet {
      Background: transparent;
      &:hover { Background: alpha(@Accent, 0.1); }
    }
  ]]></x:Styles>

  <Label x:Name="Result"/>
  <Button Text="Clear" Classes="quiet"/>
</StackPanel>
```

- The build checks the sheet: a statement it can't read is ZF2012, an error; a control or
  a property nobody has is ZF2013, a warning — the selector never matches, as the sheet
  itself would say. Values are checked when the sheet is parsed, as the element is built;
  a problem there goes to the debug output with its line in the markup.
- Write the sheet in CDATA when it uses `&` or `>` — or write them as `&amp;` and `&gt;`.
- `@import` inside it looks in the assets folder.
- Built again by Hot Reload, the element drops the old sheet before it takes the new.

`StyleSheet="…"` adds sheets from files of the assets folder, as `StyleSheet.LoadAsset`
finds them; several are separated by `;`:

```xml
<Form x:Class="MyApp.MainForm" StyleSheet="Styles/app.zss; Styles/forms.zss" …>
```

The package hands the project's `.zss` files to the build, and a sheet that isn't among
them is ZF2014 — a warning, since it may reach the assets another way. While a debugger is
attached the sheets are watched: a save restyles the running form.

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
