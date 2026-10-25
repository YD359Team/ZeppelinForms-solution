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
  a warning, as the style sheets themselves say: the selector never matches. A value is
  checked when the element is built; a problem there goes to the debug output with its line
  in the markup.
- The package hands the project's `.zss` files to the build: a `StyleInclude` of a file
  that isn't among them is ZF2014 — a warning, since it may reach the assets another way.
  While a debugger is attached an included sheet is watched: a save restyles the running
  form.
- Built again by Hot Reload, the element drops its old styles before it takes the new.

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
