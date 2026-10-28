# Controls

Every control is a `UIElement`, through one of three bases:

| Base | Holds | Examples |
| ---- | ----- | -------- |
| `UnitControl` | No children — a control in the WinForms sense | `Label`, `Button`, `TextBox`, `DataGridView` |
| `PanelControl` | Any number of children | `StackPanel`, `Grid`, `DockPanel`, `TreeView` |
| `WrapControl` | One child | `Border`, `GroupBox`, `ZoomBox`, `Spoiler` |

There are no components that are not controls: a timer is a [`DispatcherTimer`](Threading-and-Timers),
not something dropped on a form.

Every element supports padding, a margin, transparency, a box shadow, transforms,
[visual effects and blend modes](Graphics-and-Effects), a tooltip, a context menu,
[accessibility](Accessibility) and [styling](Styles-and-Themes).

## What there is

**Text and input** — `Label`, `RichLabel`, `LinkLabel`, `HintLabel`, `TextBox`,
`MaskedTextBox`, `AutoCompleteBox`, `NumericUpDown`, `MarkdownViewer`

**Buttons and choices** — `Button` (`PrimaryButton`, `SecondaryButton`, `DangerButton`),
`ToggleButton`, `SplitButton`, `CheckBox`, `RadioButton`, `ToggleSwitch`, `ComboBox`,
`CheckedComboBox`

**Ranges and progress** — `TrackBar`, `RangeSlider`, `ProgressBar`, `CircularProgressBar`,
`Loader`, `ScrollBar`

**Pickers** — `DateTimePicker`, `TimePicker`, `Calendar`, `ColorPicker`

**Lists and data** — `ListBox`, `CheckedListBox`, `DragList`, `TreeView`,
[`DataGridView`](DataGridView), `Table`, `PropertyGrid`

**Navigation** — `MenuBar`, `MenuList`, `TabControl`, `TabStrip`, `SplitView`,
`PageControl`, `PageIndicator`

**Containers** — `Panel`, `StackPanel`, `WrapPanel`, `Grid`, `DockPanel`, `UniformGrid`,
`SplitContainer`, `GridSplitter`, `Border`, `GradientBorder`, `GroupBox`, `Spoiler`,
`ZoomBox`, `GripBox`, `AdaptiveLayout`, `LayoutBuilder`, `RefreshContainer`

**Pictures and charts** — `PictureBox`, `ImageViewer`, `ImageGallery`, `SvgIcon`,
`BarChart`, `LineChart`, `PieChart`, `RadarChart`, `CandlestickChart`, `MapControl`,
the shapes (`LineShape`, `RectangleShape`, `EllipseShape`, `PolygonShape`)

The [showcase](https://yd359team.github.io/) has every one of them, with its code in
`examples/ZF SharedLib/Showcase`.

## Tooltips

Any element has one: `ToolTip` for a text, `ToolTipContent` for anything else.

```csharp
var save = new Button { Text = "Save", ToolTip = "Saves the document (Ctrl+S)" };

var rich = new Button
{
    Text = "Export",
    ToolTip = "Exports the table",          // still read by a screen reader
    ToolTipContent = new StackPanel
    {
        Orientation = Orientation.Vertical,
        Children =
        {
            new Label { Text = "Export", TextStyle = TextStyle.BodyStrong },
            new Label { Text = "CSV, the visible columns", TextStyle = TextStyle.Caption },
        },
    },
    ToolTipDelay = 300,                     // the form's ToolTipDelay otherwise, 700 ms
};
```

- It shows when the pointer rests on the element, and when the element gets the keyboard
  focus while the form is worked from the keyboard — under the element, centered. A key or
  a pointer press closes it.
- It is a `ToolTip` control: the theme gives it its surface, edge, rounding and shadow,
  and a style sheet reaches it by its type. A long text wraps at `MaxContentWidth`, 320 by
  default:

```css
ToolTip { Background: @Accent; CornerRadius: 2; MaxContentWidth: 240; }
ToolTip Label { TextColor: @TextOnAccent; }
```

- It fades in, unless motion is reduced. A control that draws parts of its own — dots,
  cells, chart points — answers `GetToolTip(point)` with the part's text.

## Writing your own

A control of another package — a PDF view, a chart, a code editor — is written against
the public API alone, as the built-in ones are. Inherit the closest base that already
does what you need:

| Base | Override | Sealed |
| ---- | -------- | ------ |
| `DecoratedControl` | `DrawContent` (required), `DrawDecoration` | `Draw` |
| `DecoratedPanel` | `DrawContent`, `DrawDecoration`, `MeasureContentOverride`, `ArrangeContentOverride` | `Draw`, `MeasureOverride`, `ArrangeOverride` |
| `DecoratedWrapControl` | `DrawContent`, `DrawDecoration` | `Draw` |

`Decorated*` draws the background, then your content, then the border. `DrawDecoration`
runs after the children and outside their clip — selection outlines, resize handles, drop
indicators.

```csharp
public partial class Swatch : DecoratedControl
{
    public static readonly PseudoClass Chosen = PseudoClass.Register("chosen");

    // in every theme, unless the application's theme has a rule of its own
    static Swatch() =>
        Theme.RegisterDefault<Swatch>(static (swatch, theme) => swatch.Fill = theme.Colors.Accent);

    [Styled(Category = "Appearance")]
    public partial Color Fill { get; set; }

    protected override void DrawContent(Graphics g) =>
        g.FillRoundRectangle(LocalBounds, CornerRadius, Fill);

    protected override Size MeasureOverride(Size availableSize) =>
        ResolveSize(new Size(32, 32), availableSize);
}
```

Any property a theme or a style sheet may set is a [styled property](Styles-and-Themes#styled-properties):
`[Styled]` on a `partial` property, and the generator does the rest.

| Need | API |
| ---- | --- |
| The form the element is in | `FindForm()` |
| Take the keyboard focus | `Focus()`, implement `IInputElement` |
| A flyout or a popup | `FindForm()?.ShowFlyout(anchor, content)` |
| A theme rule for every theme | `Theme.RegisterDefault<T>(…)` |
| A state for style sheets | `PseudoClass.Register`, `SetPseudoClass` |
| Input method composition | `ITextCompositionTarget`, `ReportTextInputArea(caret)` |
| A copy/paste menu of its own | override `GetDefaultContextMenu()` |
| Draw with Skia directly | `SkiaGraphics.Canvas` |
| A data grid column of a new kind | derive from `DataGridViewColumn<TItem, TValue>`, see [Data grid](DataGridView#columns-of-your-own) |

`tests/ZeppelinForms.ExternalControlTests` builds such controls without access to the
framework's internals, so a member they need can't quietly turn internal.
