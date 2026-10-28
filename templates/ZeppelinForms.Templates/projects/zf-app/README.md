# MyApp

A [ZeppelinForms](https://github.com/YD359Team/ZeppelinForms-solution) application for a
desktop, a browser and Android from one code.

| Project | What it is | Run it |
| ------- | ---------- | ------ |
| `MyApp` | the application: `MainForm` and its pages | — |
| `MyApp.Desktop` | Windows and Linux | `dotnet run --project MyApp.Desktop` |
| `MyApp.Browser` | WebAssembly | `dotnet run --project MyApp.Browser`, then open the address it prints |
| `MyApp.Android` | Android | `dotnet build MyApp.Android -t:Run` with a device or an emulator |

The browser project needs the `wasm-tools` workload and the Android one the `android`
workload: `dotnet workload install wasm-tools android`.

The version of the ZeppelinForms packages is `ZeppelinFormsVersion` in
`Directory.Build.props`. After moving to another one, take `zf.js` of that release into
`MyApp.Browser/wwwroot` too.

`MainForm` is a `Scaffold`: an app bar, a `NavigationHost` — a stack of pages that the
system's back, the browser's back button and a swipe from the left edge go back through —
and a navigation bar. See the wiki's
[Mobile and navigation](https://github.com/YD359Team/ZeppelinForms-solution/wiki/Mobile-and-Navigation).

`wwwroot/fonts/Inter-Regular.ttf` is under the SIL Open Font License, `wwwroot/fonts/OFL.txt`.
