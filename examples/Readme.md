# Examples

The examples are one application — the **showcase**: a `SplitView` whose pane lists every
control of the framework, its styles and effects, grouped by subject. Each page is a
self-contained file under `ZF SharedLib/Showcase/` — the place to copy a control's use from.

## Projects

- `ZF SharedLib` — all the example code: the showcase form and its pages
- `ZF Win` — the application for Windows
- `ZF Linux` — the application for Linux (X11)
- `ZF Android` — the application for Android
- `ZF Wasm` — the application for the browser (WebAssembly)

## Pages

| Group | Page | File |
|---|---|---|
| | Home | `HomePage.cs` |
| Input | Buttons · Choices · Text input · Date and color · Ranges and progress | `ButtonsPage.cs`, `ChoicesPage.cs`, `TextInputPage.cs`, `PickersPage.cs`, `RangesPage.cs` |
| Collections | Lists · Grids and tables · Tree · Gallery · Drag and drop | `ListsPage.cs`, `DataPage.cs`, `TreePage.cs`, `GalleryPage.cs`, `DragDropPage.cs` |
| Layout | Panels · Splitters and adaptive · Containers · Navigation | `PanelsPage.cs`, `SplittersPage.cs`, `ContainersPage.cs`, `NavigationPage.cs` |
| Text and graphics | Text · Icons and shapes · Charts · Map | `TextPage.cs`, `GraphicsPage.cs`, `ChartsPage.cs`, `MapPage.cs` |
| Look and feel | Styles · Effects · Animation | `StylesPage.cs`, `EffectsPage.cs`, `AnimationPage.cs` |
| Interaction | Dialogs and overlays · Gestures · Calculator | `OverlaysPage.cs`, `GesturesPage.cs`, `CalculatorPage.cs` |
| (footer) | Settings: theme, accent, language, direction, text size, motion | `SettingsPage.cs` |

Every page is built from the same pieces in `Demo.cs`: a title, a line on what the page
shows, and sections — a caption over a card with the demo and a note on what to try.

On a narrow window — a phone — the pane closes to its strip of icons and opens over the
pages; on a wide one it stays open beside them.

## Live styling

Run the example in Debug and edit `ZF SharedLib/Assets/styles.zss`: every save restyles
the Styles page at once. The same page has an editor for a style sheet that applies on the
spot — in any build, the browser included.

## RUS

Примеры — одно приложение-витрина: `SplitView`, в панели которого по категориям собраны все
контролы фреймворка, стили и эффекты. Каждая страница — отдельный самодостаточный файл в
`ZF SharedLib/Showcase/`.

- `ZF SharedLib` - весь код примеров здесь
- `ZF Linux` - приложение для Linux X11
- `ZF Win` - приложение для Windows
- `ZF Android` - приложение для Android
- `ZF Wasm` - приложение для браузера (WebAssembly)
