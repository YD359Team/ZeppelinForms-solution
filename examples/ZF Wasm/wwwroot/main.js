import { dotnet } from "./_framework/dotnet.js";

const runtime = await dotnet.create();

// zf.js обращается к экспортам через globalThis.zfExports, поэтому
// выставить их надо до запуска Main: BrowserPlatform.CreateAsync
// подгружает модуль сразу, и к первому событию он уже должен работать
const exports = await runtime.getAssemblyExports("ZeppelinForms.Browser");
globalThis.zfExports = exports.ZeppelinForms.Browser.Interop;

await runtime.runMain();