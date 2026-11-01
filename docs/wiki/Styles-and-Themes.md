# Styles and themes

The look of an application comes from three places, from the weakest:

1. **the theme** — light, dark, Fluent, Material, Cupertino, high contrast: a rule per control type;
2. **styles** — style sheets (`.zss`) or `Style` objects of the application, a form or an element;
3. **code** — a value set on an element.

A value set in code always wins: a theme never overwrites it, and switching themes leaves it be.

## Themes

```csharp
App app = new(platform) { MainForm = new MainForm(), StartupTheme = Themes.FluentLight };

App.Theme = Themes.Dark;                                 // switch at any time
App.Theme = Themes.FluentLight.WithAccent(new Color(255, 0x6F, 0x42, 0xC1));
App.UseSystemTheme(Themes.FluentLight, Themes.FluentDark); // follow the system
```

| Theme | |
| ----- | - |
| `Themes.Light`, `Themes.Dark` | The classic ones |
| `Themes.FluentLight`, `Themes.FluentDark` | WinUI 3's palette and shapes |
| `Themes.MaterialLight`, `Themes.MaterialDark` | Material 3: a scheme made from a seed color, pill buttons, outlined fields, ripples, Roboto |
| `Themes.CupertinoLight`, `Themes.CupertinoDark` | iOS: the grouped background, the system colors, fills instead of outlines, the green switch, San Francisco at 17 |
| `Themes.HighContrastBlack`, `Themes.HighContrastWhite` | Taken over by the system's high contrast mode, see [Accessibility](Accessibility) |

Each theme takes an accent its own way. A Fluent theme takes the accent's shade for its page.
A Material theme makes its whole scheme again from the accent as a seed: the primary, the
containers, and the surfaces tinted by it, the way Material You's dynamic color does. Given
the system's accent through `App.UseSystemTheme`, that is Android 12's dynamic color. A
Cupertino theme takes the accent as a tint, as iOS does, and keeps its grey selection and
its green switch.

```csharp
App.UseSystemTheme(Themes.MaterialLight, Themes.MaterialDark);       // Material You from the system's accent
App.Theme = Themes.MaterialLight.WithAccent(new Color(255, 0x00, 0x6A, 0x6A));  // a teal scheme
App.Theme = Themes.CupertinoDark.WithAccent(new Color(255, 0xAF, 0x52, 0xDE));  // a purple tint
```

Material's state layers (8 % on hover, 10 % on press) and UIKit's translucent labels and
fills are worked out in advance into opaque colors, so the themes need no alpha blending
from the renderer. The Cupertino colors are Apple's own: the secondary label and white on
the blue read below what WCAG asks of body text. Where that matters, prefer high contrast,
or give the theme an accent of your own.

The shapes and the type ramps come with the themes too: `ThemeMetrics.Material` and
`ThemeMetrics.Cupertino`, and `TypeRamp.Material` and `TypeRamp.Cupertino`. Material's
labels are `FontWeight.Medium`.

A theme is a set of rules matched by control type and applied from the base type down: a rule
for `Button` extends the one for `ButtonBase` rather than replacing it.

```csharp
App.Theme = Themes.Light.For<DataGridView>((grid, theme) =>
{
    grid.ShowAlternateRows = true;
    grid.HeaderColor = theme.Colors.SurfaceHover;
});
```

A control of another package gives itself a rule in every theme with
`Theme.RegisterDefault<T>(…)`.

## Style sheets

```css
$radius: 6;

Button.primary {
    Background: @Accent;               /* a theme color */
    CornerRadius: $radius;
    transition: Background 150ms ease-out;

    &:hover { Background: @AccentHover; }
}

#search { Padding: 8 4; CornerRadius: 4; }

DataGridView { ShowAlternateRows: true; }
```

- Selectors, a subset of CSS: a type (`Button` — and its subclasses), `*`, a class
  (`.primary`), a name (`#search`), a pseudo-class (`:hover`, `:pressed`, `:focus`,
  `:focus-visible`, `:disabled`, `:checked`, `:selected`, `:open`, `:invalid`…), `:not(…)`,
  the place among siblings (`:first-child`, `:last-child`, `:odd`, `:even`,
  `:nth-child(2n+1)`), descendants (`StackPanel Label`), children (`A > B`) and lists
  (`ListBox, TreeView`). Specificity is CSS's.
- Property names are the C# ones, or the same in kebab case. Values are written as C# would
  construct them: `Padding: 14 6`, `CornerRadius: 6`, `#0d6efd`, enum members
  (`BlendMode: multiply`). Only [styled properties](#styled-properties) can be styled —
  `Size`, `Width` and `Height` are set in code or markup.
- `@Name` is a theme color, `$name` a variable of the sheet.
- A mistake skips its declaration and is reported with its file, line and column; the rules
  that are right still apply.

```csharp
App.Styles.Load("Styles/app.zss", watch: true);         // reloaded on every save
form.Styles.Add(StyleSheet.LoadAsset("form.zss"));       // one form only
panel.Styles.Replace(StyleSheet.Parse(text).Styles);     // one subtree, from text

StyleSheet sheet = StyleSheet.Parse(text, "editor.zss");
foreach (var problem in sheet.Diagnostics)
    Console.WriteLine(problem);
```

An element's styles beat its form's, a form's beat the application's, at equal specificity.

## Styled properties

Every property a theme or a style may set is a **styled property**. A source generator
expands three lines into the registration, the backing field and the accessors:

```csharp
public partial class Swatch : DecoratedControl
{
    [Styled(Category = "Appearance", AffectsLayout = false)]
    public partial Color Fill { get; set; }

    private static Color FillDefault => Colors.Gray;
}
```

The property is `partial` with a getter and a setter, in a `partial` descendant of `UIElement`.
`AffectsLayout = true` when the value changes the measurement; `Inherits = true` when it
cascades down the tree, as `TextColor` does.

A value's precedence, highest first — `GetValueSource(property)` tells where a value came from:

1. set in code
2. a binding
3. a style
4. inherited from an ancestor (for `Inherits = true`)
5. a theme rule
6. the control's default — `SetControlDefault` in its constructor
7. the default — `<Name>Default`, or `default(T)`

`ClearValue(property)` gives a property back to the styles and the theme. A constructor
sets its defaults with `SetControlDefault`: a plain assignment there would count as code and
lock the theme out — the analyzer reports it as ZF0006.

## Transitions

Transitions belong to the property system:

```csharp
card.Transitions.Add(Transition.Ease(UIElement.BackgroundProperty, 200));
list.ChildrenEnterTransition = VisibilityTransition.Slide(220, 0f, -12f);
```

Reading a property returns the target; the in-between value is used only for drawing. With
the system's reduced motion setting the transitions are instant.
