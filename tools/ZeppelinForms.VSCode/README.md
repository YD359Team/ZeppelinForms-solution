# ZeppelinForms for VS Code

[ZeppelinForms](https://github.com/YD359Team/ZeppelinForms-solution) is a .NET 10 UI
framework. This extension brings its tooling to VS Code:

- **Completion and hover** for `.zfml` markup and `.zss` style sheets — elements after `<`,
  an element's attributes, values in quotes, the paths of `{Bind …}` from `x:DataType`, a
  sheet's controls, properties and `@ThemeColors`; hovering an element, an attribute or a
  step of a binding shows its declaration and summary.
- **Problems as you type**: the markup generator's own checks (ZF2xxx), the compiler's
  errors in the code it writes — a handler of the wrong signature — at the markup's line,
  and a sheet's unknown controls, properties and values.
- **Highlighting**: `.zfml` as XML with `{Bind …}` and `{x:Null}` picked out, `.zss` with
  its own grammar.
- **The previewer**: the project's forms and `[Preview]` methods, live — hover, click,
  type, scroll — in a panel beside the editor, reloaded after each build.

The same language server and previewer run in Visual Studio and Rider.

## Requirements

The .NET 10 runtime or newer: the language server and the previewer are .NET programs
shipped inside the extension and run by the machine's `dotnet`. Another `dotnet` is set
with `zeppelinforms.dotnetPath`.

## The preview

**ZeppelinForms: Open Preview** — the command palette, or the button in the title bar of a
`.zfml` or `.zss` editor — opens the preview of the project the active file belongs to.

```csharp
public static class ButtonPreviews
{
    [Preview("Primary", Width = 240, Height = 80)]
    public static UIElement Primary() => Buttons.Primary("Save");
}
```

- Every form with a constructor without parameters is in the list too; `[Preview]` on a
  form only sets its size, theme and the rest.
- The previews of the active file come first; **Source** opens the code of the one shown.
- The toolbar overrides a preview's size, zoom, theme, culture, right-to-left layout and
  text scale. "Fill the panel" lays the preview out at the panel's size; "Fit" zooms the
  picture to show it whole, without changing its layout.
- **After a build** — `dotnet build`, C# Dev Kit, a task — the preview reloads by itself.
  It shows the newest `bin/**/<AssemblyName>.dll` of the project; when that guess is wrong
  (an output path of your own), set `zeppelinforms.preview.assembly`.
- `Main` doesn't run in the previewer: put what it does before showing a form in a
  `[PreviewSetup]` method.
- The previewer runs the project's code, so it waits until the workspace is trusted.
  Completion and hover work in any workspace.

## Settings

| Setting | |
|---|---|
| `zeppelinforms.dotnetPath` | The `dotnet` to run the server and the previewer with. |
| `zeppelinforms.preview.assembly` | The assembly to preview, when the newest build under `bin` is not it. |
| `zeppelinforms.languageServer.path` | Another `ZeppelinForms.LanguageServer.dll`, while working on it. |
| `zeppelinforms.preview.hostPath` | Another `ZeppelinForms.Designer.dll`. |
| `zeppelinforms.trace.server` | Log the language server's messages in the ZeppelinForms output. |

## Building

From `tools/ZeppelinForms.VSCode`, with the .NET 10 SDK and Node 20 or later:

```
npm ci
npm run build      # publishes the language server and the previewer's host into the folder
npm test           # the protocol's codec against bytes .NET wrote
npm run check      # type checks of the JavaScript
npm run package    # zeppelinforms-<version>.vsix
```

F5 in VS Code opened on this folder starts an Extension Development Host with the
extension and the repository's examples. The release workflow builds the `.vsix` with the
version of the tag and attaches it to the release.

The extension is plain JavaScript with type checks (`// @ts-check`): there is no
compilation step between the sources and VS Code.

| | |
|---|---|
| `src/extension.js` | Activation, commands. |
| `src/languageServer.js` | The language client: the server over stdin and stdout. |
| `src/designer/protocol.js` | The designer protocol, `DesignerProtocol.cs` in JavaScript. |
| `src/designer/connection.js` | One host process: a Unix socket (`--socket`) or, on Windows, a named pipe (`--pipe`). |
| `src/designer/service.js` | The host for the active project, restarted after builds. |
| `src/designer/project.js` | The project of a file and its built assembly. |
| `src/preview/panel.js` | The webview panel: frames to the page, input to the host. |
| `media/preview.js` | The page: the toolbar, the picture, the mouse and the keyboard. |
