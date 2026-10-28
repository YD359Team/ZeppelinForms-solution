# Markup, previewer and Hot Reload

## Markup

Code-first is the main way to build a UI; markup is an option for the parts that read better
as a tree — a whole window included. A `.zfml` file names its class with `Class`; at build
time a source generator turns it into C# — a field for every `Name` and an
`InitializeComponent()` of plain `new`, assignments, `+=` and `Add`. No reflection, so
trimming and native AOT keep it all.

### A form in markup

The root element is the class's base type — for a window, `Form` — and nothing has to come
before it but the optional XML declaration:

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

`MainForm.zfml` next to `MainForm.cs`, which holds the behaviour:

```csharp
using ZeppelinForms.Forms.Dialogs;
using ZeppelinForms.Input.Mouse;

namespace MyApp;

public partial class MainForm
{
    private async void OnGreet(object? sender, MouseClickEventArgs e) =>
        await MessageBox.ShowAsync(this, $"Hello, {NameBox.Text}!");
}
```

and the form is run as any other:

```csharp
new App(new WindowsPlatform()) { MainForm = new MainForm() }.Run();
```

- The markup declares the class — `partial class MainForm : Form` — and its fields. The
  code part needs neither the base class nor a constructor: a class whose code declares no
  constructor gets one that calls `InitializeComponent`. One that declares its own calls
  `InitializeComponent()` itself.
- A form with no behaviour needs no code at all: the markup alone makes a public class.
- The form's own properties are attributes of the root, `<Form.Styles>` its styles,
  and its child is its `Content`.
- `x:Class`, `x:Name` and the `xmlns` lines of XAML are understood too, and needed by
  none: the file is read as if it declared the framework's namespace and `x:`. A file
  that writes `x:` without declaring it builds, but the XML editor of Visual Studio
  underlines the undeclared prefix — write `Class`/`Name`, or declare
  `xmlns:x="https://zeppelinforms.dev/markup/x"`.

`examples/ZF SharedLib/Showcase/FeedbackForm.zfml` is a dialog written this way.

### Parts of a window

Any element can be the root — a card, a page, a panel reused in several forms:

```xml
<StackPanel Class="MyApp.SignUpCard" xmlns:my="using:MyApp.Controls"
            Orientation="Vertical" Spacing="10">
  <TextBox Name="NameBox" Watermark="Your name" Size="260,NaN"/>
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

### What the markup understands

- `Name="NameBox"` names the element and makes a field of it, as `x:Name` does. A name
  that can't be a field, `Name="main-panel"`, only names the element — for `#main-panel`
  in a style sheet.

- Values are written as in style sheets: `Padding="14 6"`, `Size="260,NaN"`,
  `Background="#0d6efd"`, enum members, static members by name.
- Children go to the first property that takes them — one marked `[Content]`, then
  `Children`, `Items`, `Nodes`, `Content`, `Child`; `<Border.Child>` names one.
- `{Bind Name.Property}` binds a styled property to a named element, `Mode=TwoWay` both
  ways; `{x:Null}` is null; `<x:String>` is a text item of a list.
- `xmlns:my="using:MyApp.Controls"` brings the application's controls in as `<my:Gauge>`.
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

### Styles

`<Type.Styles>` holds styles for the element and everything inside it — the element's own
`Styles`, as if the code had added them — written as XAML writes them:

```xml
<StackPanel x:Class="MyApp.SignUpCard" …>
  <StackPanel.Styles>
    <StyleInclude Source="Styles/cards.zss"/>

    <Style Selector="#Result">
      <Setter Property="TextColor" Value="@Accent"/>
    </Style>

    <Style Selector="Button.quiet">
      <Setter Property="Background" Value="transparent"/>
      <Transition Property="Background" Duration="120ms" Easing="ease-out"/>

      <Style Selector="^:hover">
        <Setter Property="Background" Value="alpha(@Accent, 0.1)"/>
      </Style>
    </Style>
  </StackPanel.Styles>

  <Label x:Name="Result"/>
  <Button Text="Clear" Classes="quiet"/>
</StackPanel>
```

| Element | Does |
| ------- | ---- |
| `<Style Selector="…">` | A rule: the selectors of the style sheets, `Button.primary > Label:hover` |
| `<Setter Property="…" Value="…"/>` | A property and its value; the value may also be the element's text |
| `<Transition Property="…" Duration="…" Easing="…"/>` | Animates the property's changes; the easing is optional |
| `<Style>` inside a `<Style>` | A nested rule: `^` stands for the enclosing selector, `^:hover`; without `^` it is a descendant |
| `<StyleInclude Source="…"/>` | A `.zss` file of the assets folder, as `StyleSheet.LoadAsset` finds it |

- Values are written as in style sheets, so they follow the theme: `@Accent`,
  `alpha(@Accent, 0.1)`, `lighten(…)`, `Colors.RoyalBlue`, `14 6`.
- The order is the cascade's: of two rules of equal specificity the later wins, an
  included sheet included.
- The build checks the styles: a style it can't read — no `Selector`, a setter without a
  property or a value — is ZF2012, an error; a control or a property nobody has is ZF2013,
  a warning, as the style sheets themselves say: the selector never matches. The values
  are checked by the Visual Studio extension when the file is opened or saved — in the
  Error List at their lines — and when the element is built, in the debug output.
- The package hands the project's `.zss` files to the build: a `StyleInclude` of a file
  that isn't among them is ZF2014 — a warning, since it may reach the assets another way.
  While a debugger is attached an included sheet is watched: a save restyles the running
  form.
- Built again by Hot Reload, the element drops its old styles before it takes the new.

## Design apart from code

A form in code needn't mix its controls with what they do. `MainForm.design.cs` holds the
design — the controls' fields and an `InitializeComponent()` that makes, sets up, wires and
lays them out — and `MainForm.cs` the behaviour, as WinForms' `Form1.Designer.cs` and
`Form1.cs`. No designer owns the file: it is code, written and edited by hand.

```csharp
// MainForm.design.cs
partial class MainForm
{
    private TextBox _name;
    private PrimaryButton _greet;

    private void InitializeComponent()
    {
        Title = "Hello";
        Size = new Size(480, 320);

        _name = new TextBox { Watermark = "Your name" };

        _greet = new PrimaryButton { Text = "Greet" };
        _greet.Click += OnGreet;

        Content = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 10,
            Padding = new Thickness(24),
            Children = { new Label { Text = "What is your name?" }, _name, _greet },
        };
    }
}
```

```csharp
// MainForm.cs
public partial class MainForm : Form
{
    private async void OnGreet(object? sender, MouseClickEventArgs e) =>
        await MessageBox.ShowAsync(this, $"Hello, {_name.Text}!");
}
```

What the framework adds to a `*.design.cs`, as it does to markup:

- **A constructor** that calls `InitializeComponent()`, when the class declares none. A
  constructor of the class's own calls it itself — ZF0007 warns of one that doesn't, since
  its form would open empty with its fields null.
- **Hot Reload**: `InitializeComponent()` runs again after an edit, so a change of the
  design shows in the open form. That is why the controls are made inside it rather than
  in the fields' initializers, and their events wired there too: run again, it makes fresh
  controls with one handler each, where controls made once would get every handler twice.
  A class that overrides `OnHotReload` itself keeps its own. A control whose design adds to
  its `Children` clears them first.
- **No CS8618** for the fields `InitializeComponent()` assigns: the compiler doesn't follow
  the call from the constructor, the ZeppelinForms analyzers do. A field the method
  forgets keeps the warning.
- **Nesting**: `MainForm.design.cs` and `MainForm.zfml` show under `MainForm.cs` in
  Solution Explorer and Rider (the package's build targets set `DependentUpon`).

The showcase's `OrderForm.design.cs` and `OrderForm.cs` are a whole example; `dotnet new
zf-form` makes the pair.

## Analyzers

The `ZeppelinForms` package brings analyzers that catch the framework's own pitfalls at build
time — ZF0xxx for code (an assignment in a constructor that locks the theme out, a styled
property declared wrong, a constructor that doesn't build its design part…) and ZF2xxx for
markup.

## Previewer

The **ZeppelinForms Tools** extension for Visual Studio 2022 (17.10 and later) and Visual
Studio 2026 — the `.vsix` is attached to every release.

Ctrl+Alt+Shift+P (View › Other Windows › ZeppelinForms Preview) previews the active project:
every form with a parameterless constructor and every method marked `[Preview]` (namespace
`ZeppelinForms.Design`). What `Main` does before showing a form — loading style sheets,
choosing a theme — goes into a `[PreviewSetup]` method. The extension also highlights `.zss`
style sheets and lists their problems in the Error List, and those of the values of the
styles in `.zfml` markup.

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
`watch: true` follow their saves by themselves; markup and a
[design part](#design-apart-from-code) are built again after an edit with no code of one's own.

## The inspector

F12 or Ctrl+Shift+I over a running form: the element under the cursor with its margin and
padding, its properties in a property grid (editable), and the [accessibility](Accessibility)
audit. It opens as an overlay or docked to the side.
