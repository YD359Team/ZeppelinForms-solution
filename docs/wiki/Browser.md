# The browser

`ZeppelinForms.Browser` runs the same forms in WebAssembly, drawn into a canvas with
SkiaSharp. The [showcase](https://yd359team.github.io/) is such an application —
`examples/ZF Wasm` in the repository.

## Starting

```csharp
public static class Program
{
    static Task Main()
    {
        // characters the font lacks: added before RunAsync, which downloads them
        FontFallbacks.Add("/fonts/NotoSansHebrew-Regular.ttf");
        FontFallbacks.Add("/fonts/NotoSansJP-Regular.otf");

        return BrowserApp.RunAsync(
            () => new MainForm(),
            font: "/fonts/Inter-Regular.ttf",
            preload: ["/Assets/Logo.png"]);
    }
}
```

| Parameter | |
| --------- | - |
| `mainForm` | A factory: a form measures text in its constructor, and the text measurer exists only once the platform is made |
| `font` | The font file — also its address on the server. Becomes `Font.Default` |
| `preload` | Files needed synchronously later — pictures for `Image.LoadAsset` and the like |
| `fontFamily`, `fontSize` | The default font's family (from the file name by default) and size |
| `canvasId` | The canvas to draw into, `zf-canvas` by default |
| `theme` | The theme to start with |

The browser has no file system: `BrowserApp` downloads `font`, `preload` and the
`FontFallbacks` into a virtual one before the first form is built, after which
`Image.LoadAsset` and `Font.WithFile` work as on the desktop. A file the server doesn't have
fails the start with a message in the browser's console.

## Host files

`wwwroot/index.html`:

```html
<body>
    <canvas id="zf-canvas"></canvas>
    <script type="module" src="main.js"></script>
</body>
```

`wwwroot/main.js`:

```javascript
import { dotnet } from "./_framework/dotnet.js";
import * as zf from "./zf.js";

const runtime = await dotnet.create();
runtime.setModuleImports("zf", { ...zf });

const exports = await runtime.getAssemblyExports("ZeppelinForms.Browser");
globalThis.zfExports = exports.ZeppelinForms.Browser.Interop;

await runtime.runMain();
```

`zf.js` ships with `ZeppelinForms.Browser`; copy it into the application's `wwwroot`.

## Fonts

A browser gives Skia **no fonts at all**. The text is drawn with the one font given to
`BrowserApp`, and a character it lacks — Hebrew, Arabic, CJK, emoji in a Latin font — is
drawn as an empty box unless a fallback has it:

1. Put the font file in `wwwroot/fonts/` (with its license — the Noto fonts are under the
   SIL Open Font License, `OFL.txt`).
2. Add it with `FontFallbacks.Add("/fonts/…")` before `BrowserApp.RunAsync`.

Fallbacks are searched in the order added. CJK fonts are large — several megabytes — and
every visitor downloads them on the first visit: a subset with the characters the
application needs is lighter.

## What differs

- **Dialogs are async-only**: there is no nested loop, so `ShowDialogAsync`,
  `MessageBox.ShowAsync`, `InputBox.ShowAsync`.
- **No system drag and drop** — drag and drop inside the application works.
- **No windows**: one form fills the canvas; `Position`, `TopMost` and the size limits are
  accepted and do nothing.
- **File pickers** are the browser's own, through `IFilePicker`.
- Trimmed publishing (`PublishTrimmed`) is supported: the framework is annotated for the
  trimmer.

## Publishing to GitHub Pages

`dotnet publish -c Release` and serve `publish/wwwroot`. The showcase's workflow
(`yd359team.github.io/.github/workflows/showcase.yml`) builds `examples/ZF Wasm` from the
framework's `master` by default; run it by hand with another `ref` to see a branch before
it is merged.
