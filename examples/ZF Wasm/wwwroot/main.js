import { dotnet } from "./_framework/dotnet.js";
import * as zf from "./zf.js";

const runtime = await dotnet.create();

// zf.js — копия из ZeppelinForms.Browser, лежит рядом в wwwroot.
// Спред, а не сам объект модуля: пространство имён модуля —
// экзотический объект, а рантайм ждёт обычный словарь функций
runtime.setModuleImports("zf", { ...zf });

// zf.js обращается к экспортам через globalThis.zfExports,
// поэтому выставить их надо до запуска Main
const exports = await runtime.getAssemblyExports("ZeppelinForms.Browser");
globalThis.zfExports = exports.ZeppelinForms.Browser.Interop;

await runtime.runMain();