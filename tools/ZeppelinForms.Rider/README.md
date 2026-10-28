# ZeppelinForms for Rider

The ZeppelinForms plugin for JetBrains Rider 2025.2.1 and later:

- **Completion, quick documentation and problems** for `.zfml` markup and `.zss` style
  sheets, from the ZeppelinForms language server — the one Visual Studio and VS Code run —
  through Rider's LSP support. Elements after `<`, attributes, values, the paths of
  `{Bind …}` from `x:DataType`, a sheet's controls, properties and `@ThemeColors`; the markup
  generator's own checks and the compiler's errors in the code it writes, at the markup's line.
- **Highlighting**: `.zfml` opens in the XML editor; `.zss` has the grammar the other two
  editors have, as a TextMate bundle.
- **The previewer**: View › Tool Windows › **ZeppelinForms Preview** shows the project's
  forms and `[Preview]` methods, live — hover, click, type, scroll —, reloaded after each build.

It needs the .NET 10 runtime or newer: the server and the previewer are .NET programs inside
the plugin, run by the machine's `dotnet` — `ZEPPELINFORMS_DOTNET`, `DOTNET_ROOT`'s, the
`PATH`'s, or where the installers put it.

## The preview

The tool window shows the previews of the project the selected editor's file belongs to: the
nearest `.csproj` above it, and the newest `bin/**/<AssemblyName>.dll` of that project. A newer
build — Rider's, or `dotnet build` in the terminal — restarts the previewer, and the window
shows again what it showed.

- Every form with a constructor without parameters is in the list, with the `[Preview]`
  methods; the previews of the selected file come first.
- The toolbar sets the size the preview is laid out at (its own, a device's, or the window's),
  the zoom (the picture only: the host draws the same layout with more or fewer pixels, so it
  stays sharp), the theme, the culture, right-to-left layout and the text scale.
- `Main` doesn't run in the previewer: put what it does before showing a form in a
  `[PreviewSetup]` method.
- Shortcuts of the IDE keep working while the picture has the focus: they go to Rider first.
  Typing, Tab, the arrows and Enter go to the preview.

## How it works

| | |
|---|---|
| `lsp/ZfLspServerSupportProvider.kt` | The language server for `.zfml` and `.zss`, over stdin and stdout. The LSP names before 2026.1.4: renamed since, deprecated, and still working. |
| `textmate/ZfTextMateBundleProvider.kt` | `.zss`'s grammar, in `textmate/zss` of the plugin's folder. |
| `designer/DesignerProtocol.kt` | The designer protocol — `DesignerProtocol.cs` — in Kotlin. |
| `designer/DesignerConnection.kt` | One host process: the plugin listens on a Unix domain socket and starts `ZeppelinForms.Designer --socket <path>`. Plain JDK. |
| `designer/DesignerService.kt` | The host for the active project; a newer build of its assembly restarts it. |
| `designer/ProjectOutput.kt` | The project of a file and its built assembly. |
| `preview/PreviewPanel.kt` | The tool window: the toolbar, the picture, the mouse and the keyboard. |
| `preview/KeyCodes.kt` | Java's key codes as Windows virtual-key codes, ZeppelinForms' `Key`. |

A Unix socket on every system, Windows too (10 1803 and later): .NET's named pipes are
sockets on Linux and macOS as well, but under a name .NET picks, and the JVM has no named
pipes on Windows. The host's `--socket` takes the path as the plugin chose it.

## Building

With JDK 21 and the .NET 10 SDK, from `tools/ZeppelinForms.Rider`:

```
./gradlew buildPlugin      # build/distributions/zeppelinforms-rider-<version>.zip
./gradlew test             # the codec against bytes .NET wrote, the key codes, the project's output
./gradlew runIde           # a Rider with the plugin
```

The build publishes the language server and the previewer's host (`dotnet publish`) into the
plugin's `server/` and `designer/` folders, and copies `.zss`'s grammar from
`tools/ZeppelinForms.VisualStudio/Grammars`. `-PzfVersion=…` gives the .NET programs a
version: the release workflow passes the tag's, as for the Visual Studio extension — a host
of another version can't bind a project built against the framework.

`ZF_DESIGNER_HOST=<…/ZeppelinForms.Designer.dll> ZF_PREVIEW_ASSEMBLY=<…/App.dll> ./gradlew test`
adds a whole conversation with a real host to the tests; CI runs it with the examples' library.

Install the zip with Settings › Plugins › ⚙ › Install Plugin from Disk. The release workflow
attaches it to the GitHub release and, with `JETBRAINS_MARKETPLACE_TOKEN` set, publishes it to
the JetBrains Marketplace.

The Rider version to build against is `riderVersion` in `gradle.properties`.
