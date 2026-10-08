# ZeppelinForms Tools for Visual Studio

The previewer — live previews of `[Preview]` methods and forms — and `.zss` style
sheets: highlighting and problems in the Error List. Visual Studio 2022 17.10 and
later, and Visual Studio 2026.

## Using it

**View › Other Windows › ZeppelinForms Preview** (Ctrl+Shift+Alt+P) opens the
preview of the project whose document is active.

```csharp
public static class ButtonPreviews
{
    [Preview("Primary", Width = 240, Height = 80)]
    public static UIElement Primary() => Buttons.Primary("Save");

    [Preview("Primary, dark", Theme = "FluentDark")]
    public static UIElement PrimaryDark() => Buttons.Primary("Save");
}
```

- Every form of the project with a constructor without parameters is in the list
  too; `[Preview]` on a form only sets its size, theme and the rest.
- The window shows the previews of the active document first.
- The toolbar overrides a preview's theme, size, culture, right-to-left layout and
  text scale. "Fit the window" sizes the preview to the window.
- The preview is live: hover, click, type, scroll. Animations and transitions run.
- **After a build** of the project the preview reloads by itself. Edits of `.zss`
  sheets the project loads with `watch: true` show up at once, without a build.
- `Main` doesn't run in the previewer. Put what it does before showing a form —
  loading style sheets, choosing a theme — in a `[PreviewSetup]` method.
- A preview that throws shows its exception instead of the picture.

`.zss` files are highlighted, and their problems — unknown properties, values that
can't be read, unknown control types and pseudo-classes — go to the Error List when
a sheet is opened or saved.

## How it works

The extension starts a host process, `ZeppelinForms.Designer`, shipped inside the
extension and run by the machine's .NET (10 or later). The host loads a copy of the
project's build, finds its previews, lays them out and draws them with Skia, and
talks to the extension over a named pipe in the designer protocol
(`src/ZeppelinForms.Design/Protocol/DesignerProtocol.cs`, shared by both sides).

A build starts a new host rather than reloading the old one: the framework's
registries would keep every old version of the project's controls. A host starts in
a fraction of a build's time.

The host uses its own copy of ZeppelinForms. A project built against another
version of the framework is loaded with a warning in the status line.

## Building

On Windows, with Visual Studio 2022 17.10 or later and the .NET 10 SDK: open
`ZeppelinForms.VisualStudio.sln` and build. The build publishes the host into the
extension with `dotnet publish`. F5 starts the experimental instance of Visual Studio
with the extension installed.

The extension is not part of the main solution: it builds with Visual Studio's
MSBuild only, and CI builds the main solution with `dotnet` on Linux as well.

## The protocol

Plain enough for a non-.NET client — a Rider plugin is the next one:

```
[int32 length][byte kind][payload]     little-endian; length counts kind + payload
```

Strings are those of .NET's `BinaryWriter`: a 7-bit encoded byte count and UTF-8.
The IDE sends `Hello`, `Load`, `Open`, `Settings`, `Pointer`, `Key`, `Text`,
`CheckSheet` and `Shutdown`; the host answers with `Hello`, `Catalog`, `Frame`
(RGBA, premultiplied), `PreviewError`, `SheetDiagnostics` and `Log`. See the
protocol file for the fields of each.