# AGENTS.md

Instructions for AI coding agents working in this repository: Claude Code, Codex,
Copilot, Cursor and the like. Humans are welcome to read along. The full documentation is
the wiki, kept in `docs/wiki`.

## What this is

ZeppelinForms (ZF) is a code-first, cross-platform UI framework for .NET 10. It takes the
simplicity of WinForms and a few ideas from WPF, Avalonia and Flutter. One UI runs on
Windows, Linux (X11 and Wayland), the browser (WebAssembly) and Android.

| Path | What it is |
| ---- | ---------- |
| `src/ZeppelinForms` | The framework itself: controls, layout, input, styling, themes. It knows no platform and no graphics library. |
| `src/ZeppelinForms.Skia`, `src/ZeppelinForms.Gles` | The renderers: SkiaSharp, and OpenGL ES with a shader of its own. |
| `src/ZeppelinForms.Windows`, `.Linux`, `.Browser`, `.Android` | The platforms: windows, input, clipboard and dialogs, each from its system. |
| `src/ZeppelinForms.Analyzers` | Source generators (styled properties, `.zfml` markup, style types) and the ZF diagnostics. |
| `tests/ZeppelinForms.UnitTests` | The tests: xUnit v3 on Microsoft.Testing.Platform, against the headless platform. |
| `tests/ZeppelinForms.ExternalControlTests` | Controls written against the public API alone, as another package would write them: a member they need that turns internal fails this build. |
| `examples/ZF SharedLib` | The showcase: one page per area in `Showcase/`, listed in `ExampleMainForm.cs`. `ZF Win`, `ZF Linux`, `ZF Wasm` and `ZF Android` start it on each platform. |
| `templates/ZeppelinForms.Templates` | The `dotnet new` templates: `zf-app`, `zf-form`, `zf-form-markup`, `zf-stylesheet`. |
| `tools/` | The editors' extensions (Visual Studio, VS Code, Rider), the designer, the language server, and `ship-api.cs`. |
| `docs/wiki` | The wiki's source, published by `.github/workflows/wiki.yml`. |

## Building and testing

```bash
# everything; the browser and Android projects need: dotnet workload install wasm-tools android
dotnet build "ZeppelinForms solution.slnx"

# Windows without those workloads, as CI builds it
dotnet build .github/ci/windows.slnf

# the tests; on Linux without a display, under xvfb-run -a
dotnet test --project tests/ZeppelinForms.UnitTests/ZeppelinForms.UnitTests.csproj

# one class, or one test
dotnet test --project tests/ZeppelinForms.UnitTests/ZeppelinForms.UnitTests.csproj -- --filter-class ZeppelinForms.UnitTests.DockHostTests
dotnet test --project tests/ZeppelinForms.UnitTests/ZeppelinForms.UnitTests.csproj -- --filter-method "*TabDraggedOntoTheCompass*"
```

The build is strict, and a change is not done until it builds clean:

- `TreatWarningsAsErrors` is on everywhere, so a warning fails the build.
- The core runs the .NET analyzers at `latest-recommended`. Its deliberate departures are
  in `src/ZeppelinForms/ZeppelinForms.globalconfig`, each with its reason: add a departure
  there only with a reason.
- The ZF diagnostics apply to the framework's own code too:
  - ZF0001: a new `ObservableCollection` assigned over one that may have subscribers;
  - ZF0002: an overload shadowed by another;
  - ZF0003–ZF0005: a `[Styled]` property, or its type, that isn't `partial` or isn't a
    `UIElement`;
  - ZF0006: a styled property assigned in a constructor;
  - ZF0007: a constructor that doesn't build its design part.

## The public API

Only `src/ZeppelinForms` tracks its public API, in `PublicAPI.Shipped.txt` and
`PublicAPI.Unshipped.txt`, checked by `Microsoft.CodeAnalysis.PublicApiAnalyzers`.

- **Every public or protected member you add** gets a line in `PublicAPI.Unshipped.txt`,
  in the analyzer's format and in sorted order. Otherwise the build fails with RS0016.
- **A change to a shipped member** is a removal and an addition: a `*REMOVED*` line with
  the old signature, then the new one.
- **Never edit `PublicAPI.Shipped.txt`.** `dotnet run tools/ship-api.cs` moves the
  lines when a release begins.
- **No two overloads with optional parameters** (RS0026), and no overload with more
  parameters than the one that has the optional ones (RS0027). Give the new method its own
  name, or split it into overloads without optional parameters.

## Writing framework code

- **Controls** derive from `UnitControl` (no children), `PanelControl` (children) or
  `WrapControl` (one child). Use their `Decorated*` variants when the control needs a
  background, a border or corner radii.
  - `Draw` is sealed in the `Decorated*` classes. Override `DrawContent` (under the
    children) and `DrawDecoration` (over them, outside their clip).
  - `Invalidate()` asks for layout, `InvalidateVisual()` only for a repaint.
- **Any value a theme may set** is a styled property:
  `[Styled] public partial Color HoverColor { get; set; }`, with a static
  `HoverColorDefault`.
  - A constructor sets such a value with `SetControlDefault(Property, value)`: a plain
    assignment would lock the theme out (ZF0006).
  - A new color gets a rule in `src/ZeppelinForms/Theming/Themes.cs` and in
    `Theme.HighContrast.cs`.
- **A collection a control exposes** is a `ZfCollection<T>`. Use its batch methods
  (`AddRange`, `ReplaceAll`, `RemoveAll`, `Sort`) for many items at once. The control
  follows its changes, and keeps the item that something was selected by rather than the
  index.
- **Subscriptions**: a control that subscribes to anything outside itself does it in
  `OnAttached` and unsubscribes in `OnDetached`. A tree out of its form must hold nothing
  of the data it showed.
- **A drag** takes the pointer with `CaptureMouse()` and lets it go on release. In
  `OnPointerCanceled` it puts back what it changed: nothing was committed.
- **User-facing text** is a `TextKey` in `Core/Globalization/ZfText.cs`, with its Russian
  in `ZfRussianStrings.cs`.
- **AOT and trimming**: the core is `IsAotCompatible`. No reflection over user types, and
  JSON is read and written by hand with `Utf8JsonWriter` and `JsonDocument`.
- **No new package dependencies in the core**: graphics and platforms live in their own
  projects.

## Tests

- The headless platform runs a form as a window would. Use `new HeadlessPlatform().CreateWindow(form)`,
  then `form.UpdateLayout()`.
- Tests that open forms go into `[Collection("Platform")]`: they share the UI thread's state
  and must not run in parallel.
- **Input**: `HeadlessInput` is the public way in. The tests also reach `Form`'s internal
  entry points (`form.OnPointerDown(point)`, `form.OnKeyDown(key, modifiers, false)`)
  through `InternalsVisibleTo`.
- **Drawing**: check what was drawn with a `HeadlessGraphics` subclass that records the
  calls, drawn through `ElementTreeRenderer.Draw`.
- **xUnit v3 analyzers fail the build** too:
  - await rather than block: no `.Result` or `.GetAwaiter().GetResult()` (xUnit1031);
  - pass `TestContext.Current.CancellationToken` to whatever takes a token (xUnit1051).
- **A bug fix comes with a test** that fails without it.
- **Snapshots**: the reference renders in `Snapshots/Expected/{win,linux}` change only on
  purpose, with `ZF_UPDATE_SNAPSHOTS=true`.

## Docs and the changelog

- `CHANGELOG.md` has a section for the version being made (`<Version>` in
  `Directory.Build.props`).
  - *Breaking changes* come first, each with what to do instead.
  - Then one heading per area, with what changed as the user sees it.
- A new feature gets its page or section in `docs/wiki`, linked from `_Sidebar.md` and
  `Home.md`. Wiki links are page names without `.md`: `[Layout](Layout)`.
- A new control or a visible feature gets a demo in the showcase:
  `examples/ZF SharedLib/Showcase/`, registered in `ExampleMainForm.cs`.

## Style

- File-scoped namespaces. `.cs` files are UTF-8 with a BOM, and an edit keeps the file's
  BOM and line endings as they are.
- Comments and docs are in English.
  - A comment says why the code is as it is, and often what went wrong before it was.
  - XML docs say what a member is for, in plain sentences.
- **Keep the existing comments and `#region`s.** Remove one only when what it says is no
  longer true. When code changes, its comment changes with it.
- Names say what things are. Tests are named as sentences about the behaviour:
  `AHiddenItemGoesBackWhereItWas`.

## What not to do

- Don't commit, push, tag or open pull requests unless you are asked to: the maintainer
  reviews and commits the changes himself.
- Don't edit `PublicAPI.Shipped.txt`, the analyzer release files of shipped versions, or
  the snapshots, unless the task is exactly that.
- Don't silence a warning or a diagnostic to make the build pass. Fix its cause, or add the
  departure to the globalconfig with its reason.
- Don't widen the scope: a fix changes what it fixes, and a refactoring is a task of its own.
