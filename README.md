# ZeppelinForms

![Logo](ZF_medium.png)

**ZeppelinForms** (ZF) is an experimental project aimed at creating a simple, platform-independent UI framework with hardware acceleration (on Windows) and straightforward code-behind UI development.

### ⚙️ Current Status

![CI](https://github.com/YD359Team/ZeppelinForms-solution/actions/workflows/ci.yml/badge.svg)

The project is under active development.

| # | Name        | Status |
| - | ----------- | ------ |
| 1 | Headless    | ✅      |
| 2 | Windows     | ✅      |
| 3 | Linux (X11) | ✅*     |
| 4 | WebAssembly | ✅**    |
| 5 | macOS       | 💡     |

\* — Linux: `SetOpacity` and `SetWindowState` are not implemented yet: they need
`_NET_WM_WINDOW_OPACITY` and `_NET_WM_STATE`. 
\*\* - WebAssembly: dialogs are async-only,
and system drag and drop is not supported — see the browser section below.

### 🧠 Philosophy

In short: combine the simplicity of WinForms with selected ideas from WPF and Flutter, the cross-platform capabilities of Avalonia, and get rid of tons of legacy baggage along the way.

* No dependency on a specific platform
* No dependency on a specific graphics stack

And if the project is modern, why not take advantage of the full capabilities of .NET 10 and C# 14?

### 🖌️ Rendering

| # | Name      | Status |
| - | --------- | ------ |
| 1 | SkiaSharp | ✅      |
| 2 | DirectX   | 💡     |

ZeppelinForms itself knows nothing about Skia, because it is implemented in a separate project, `ZeppelinForms.Skia`. This means that the graphics layer is completely decoupled from the framework logic.

As a result, the framework can be integrated with DirectX or virtually any other graphics stack.

If hardware acceleration is unavailable, the framework falls back to software rendering.

### 📟 Forms

**Forms** are the only type of window in ZeppelinForms, just like in WinForms.

| # | Name                  | Status |
| - | --------------------- | ------ |
| 1 | Debugger              | ✅*     |
| 2 | Overlays              | ✅**    |
| 3 | Toast Notifications   | ✅***   |
| 4 | ToolTips              | ✅      |
| 5 | Dialog Windows        | ✅      |
| 6 | Open/Save File Dialog | ✅****  |
| 7 | MessageBox            | ✅      |
| 8 | InputBox              | ✅      |
| 9 | Clipboard             | ✅      |
| 10 | Folder Dialog        | ✅****  |
| 11 | Drag&Drop (internal) | ✅      |

\* — the inspector currently works only with certain types \
\*\* — the API will be extended further \
\*\*\* — some limitations and unfinished parts remain \
\*\*\*\* - managed implementation by default; a platform may supply a system picker through `IFilePicker`, as the browser backend does

### 🛣️ Layout

* Supports `Measure` and `Arrange`
* All controls support docking
* Supports horizontal and vertical alignment, including content alignment

### 🧩 Controls

All controls must inherit from `UIElement`, either directly or indirectly through `UnitControl`, `PanelControl`, or `WrapControl`.

There are no WinForms-style components that are not considered actual controls, such as `Timer` or `BackgroundWorker`.

#### UIElement

The common base type for all controls. A form is not a `UIElement`, but its `Content` can be any `UIElement`.

Unlike WinForms, all controls support:

* Internal padding
* Transparency
* Shadows (`box-shadow`)
* Scaling

#### Base class hierarchy

`UIElement` defines geometry, input and painting entry points. Decoration —
background, corner radius, border — lives one level down. Inherit the closest
base that already does what you need:


`Draw` is sealed in every `Decorated*` class: it fills the background, calls
your content, then draws the border. Override these instead:

| Base | Override | Sealed |
| ---- | -------- | ------ |
| `DecoratedControl` | `DrawContent` (required), `DrawDecoration` | `Draw` |
| `DecoratedPanel` | `DrawContent`, `DrawDecoration`, `MeasureContentOverride`, `ArrangeContentOverride` | `Draw`, `MeasureOverride`, `ArrangeOverride` |
| `DecoratedWrapControl` | `DrawContent`, `DrawDecoration` | `Draw` |

`DrawContent` runs before children, `DrawDecoration` after them and outside
their clip — that is where selection outlines, resize handles and drop
indicators go. For state-dependent colors override `CurrentBackground` and
`CurrentBorderColor` rather than painting the background yourself.

`Shape` is the one deliberate exception: shapes have their own `Fill` and
`Stroke`, so an inherited `Background` would only confuse.

#### Styled Properties

Any property a theme may set must be declared as a styled property. A plain
auto-property gets overwritten by the theme, is invisible to `PropertyGrid`
and does not trigger a repaint. A source generator expands three lines into
the registration, the backing field and the accessors:

```csharp
[Styled(Category = "Menu")]
public partial Color HoverColor { get; set; }

private static Color HoverColorDefault => new(255, 232, 240, 254);
```

Requirements: the property is `partial` with a getter and a setter, its type
is a `partial` descendant of `UIElement`. The default comes from a static
property named `<Name>Default`; omit it when `default(T)` will do. It must be
a property, not a field: static field initializers run in declaration order,
and partial declarations are split across files, so a field could be read
before it is computed.

Flags: `AffectsLayout = true` when the value changes measurement — the setter
then invalidates layout instead of only repainting. `Inherits = true` when the
value cascades down the tree, as `TextColor` does.

Value precedence, highest first:

1. set from user code
2. set from user code on any ancestor, for inherited properties
3. theme or style
4. control default — `SetControlDefault` from a constructor
5. `DefaultValue` from the registration

A constructor must use `SetControlDefault`: a plain assignment there would mark
the value as user-set and lock the theme out for good.

#### Unit Controls

`UIElement` → `UnitControl`

**Unit controls** are similar to `Control` in WinForms and can be thought of as regular controls. They cannot contain child controls.

|  № | Name            | Status |  № | Name        | Status |
| -: | ------------------- | :----: | -: | --------------- | :----: |
|  1 | Label               |    ✅   | 21 | ToggleButton    |    ✅   |
|  2 | Button              |    ✅   | 22 | BarChart        |    ✅   |
|  3 | CheckBox            |    ✅   | 23 | LineChart       |    ✅   |
|  4 | PictureBox          |    ✅   | 24 | PieChart        |    ✅   |
|  5 | RadioButton         |    ✅   | 25 | RichLabel       |    ✅   |
|  6 | TextBox             |   ✅*   | 26 | LinkLabel       |    ✅   |
|  7 | ToggleSwitch        |    ✅   | 27 | LineShape       |    ✅   |
|  8 | DateTimePicker      |    ✅   | 28 | RectangleShape  |    ✅   |
|  9 | TimePicker          |    ✅   | 29 | EllipseShape    |    ✅   |
| 10 | ColorPicker         |    ✅   | 30 | PolygonShape    |    ✅   |
| 11 | ScrollBar           |    ✅   | 31 | CheckedComboBox |    ✅   |
| 12 | SvgIcon             |    ✅   | 32 | ComboBox        |    ✅   |
| 13 | NumericUpDown       |    ✅   | 33 | GridSplitter    |    ✅   |
| 14 | ProgressBar         |    ✅   | 34 | MaskedTextBox   |    ✅   |
| 15 | CircularProgressBar |    ✅   | 35 | HintLabel       |    ✅   |
| 16 | TrackBar            |    ✅   | 36 | MapControl      |    ✅   |
| 17 | Calendar            |    ✅   | 37 | Loader          |    ✅   |
| 18 | MenuBar             |    ✅   | 38 | PageIndicator   |    ✅   |
| 19 | MenuList            |    ✅   |    |                 |         |
| 20 | SplitButton         |    ✅   |    |                 |         |


* — contains bugs and is missing part of its API

#### Panels

`UIElement` → `PanelControl`

**Panels** are controls that can contain other controls, including other panels.

| # | Name                   | Status |
| - | ---------------------- | ------ |
| 1 | Panel                  | ✅      |
| 2 | StackPanel             | ✅      |
| 3 | Grid                   | ✅      |
| 4 | DockPanel              | ✅      |
| 5 | TabControl             | ✅      |
| 6 | UniformGrid            | ✅      |
| 7 | VirtualizingStackPanel | ✅      |
| 8 | SplitContainer         | ✅      |
| 9 | PageControl            | ✅      |
| 10 | WrapPanel             | ✅      |
| 11 | Table                 | ✅      |
| 12 | AttachButton          | ✅      |
| 13 | PropertyGrid          | ✅      |

##### Items Panels

`UIElement` → `PanelControl` → `ItemsControl`

A specialized type of panel capable of working with collections of elements.

| # | Name           | Status |
| - | -------------- | ------ |
| 1 | ListBox | ✅ |
| 2 | CheckedListBox | ✅ |
| 3 | DragList | ✅ |
| 4 | TreeView | 💡 |
| 5 | DataGrid | 💡 |

⭐ All panels can display a scrollbar when their content overflows.

#### Wrapper Controls

`UIElement` → `WrapControl`

**Wrapper controls** are controls that can contain a single child control. This concept is unusual in the WinForms world, but familiar from XAML-based frameworks.

| # | Name           | Status |
| - | -------------- | ------ |
| 1 | Border         | ✅    |
| 2 | Spoiler        | ✅    |
| 3 | ZoomBox        | ✅    |
| 4 | GroupBox       | ✅    |
| 5 | LayoutBuilder  | ✅    |
| 6 | Page           | ✅    |
| 7 | GradientBorder | ✅    |
| 8 | GripBox        | ✅    |

### 🎄 Themes

Built-in light and dark themes are included. A theme is a set of appliers
matched by control type; they are applied from the base type down, so a
specific applier extends the base one instead of replacing it.

A theme never overwrites a value set from user code — see Styled Properties
above for the full precedence. `ClearValue` gives a property back to the theme.

### 🛠️ Code Examples

Creating an application in Windows:

```csharp
public class Program
{
    static void Main()
    {
        WindowsPlatform windowsPlatform = new();
        App myApp = new(windowsPlatform)
        {
            MainForm = new MainForm()
        };
        myApp.Run();
    }
}
```

Creating an application in Linux (X11):

```csharp
public class Program
{
    static void Main()
    {
        X11Platform linuxPlatform = new();
        App myApp = new(linuxPlatform)
        {
            MainForm = new MainForm()
        };
        myApp.Run();
    }
}
```

Creating an application in the browser (WebAssembly):

```csharp
public class Program
{
    static Task Main() => BrowserApp.RunAsync(
        () => new MainForm(),
        font: "/fonts/Inter-Regular.ttf",
        preload: ["/Assets/Logo.png"]);
}
```

A factory is passed rather than a form instance: a form measures text in its
constructor, and the text measurer only exists once the platform has been created.

The browser has no system fonts and no local file system, so `font` and `preload`
name files served from `wwwroot`. `BrowserApp` downloads them into the virtual
file system before the first form is built, after which `Image.LoadAsset` and
`Font.WithFile` work exactly as they do on the desktop.

Two host files are needed alongside the application. `wwwroot/index.html`:

```html
<body>
    <canvas id="zf-canvas"></canvas>
    <script type="module" src="main.js"></script>
</body>
```

and `wwwroot/main.js`, which loads the runtime and wires up the JavaScript module:

```javascript
import { dotnet } from "./_framework/dotnet.js";
import * as zf from "./zf.js";

const runtime = await dotnet.create();
runtime.setModuleImports("zf", { ...zf });

const exports = await runtime.getAssemblyExports("ZeppelinForms.Browser");
globalThis.zfExports = exports.ZeppelinForms.Browser.Interop;

await runtime.runMain();
```

`zf.js` ships with `ZeppelinForms.Browser` and has to be copied into the
application's `wwwroot`. See `examples/ZF Wasm` for a complete project.

### 🧪 Snapshot Tests

Reference snapshots are stored in:

`tests/ZeppelinForms.UnitTests/Snapshots/Expected/{win,linux}`

Text rendering differs between platforms, so separate snapshot sets are maintained for Windows and Linux.

Local snapshot update:
- bash: `ZF_UPDATE_SNAPSHOTS=true dotnet test`
- PowerShell: `$env:ZF_UPDATE_SNAPSHOTS='true'; dotnet test`