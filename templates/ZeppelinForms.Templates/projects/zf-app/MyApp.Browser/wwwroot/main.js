import { dotnet } from "./_framework/dotnet.js";
import * as zf from "./zf.js";

const runtime = await dotnet.create();

// a spread, not the module itself: the runtime takes a plain object of functions
runtime.setModuleImports("zf", { ...zf });

// zf.js calls back into .NET through globalThis.zfExports: set before Main runs
const exports = await runtime.getAssemblyExports("ZeppelinForms.Browser");
globalThis.zfExports = exports.ZeppelinForms.Browser.Interop;

await runtime.runMain();
