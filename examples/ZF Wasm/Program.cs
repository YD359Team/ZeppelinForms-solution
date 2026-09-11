using ZeppelinForms;
using ZeppelinForms.Browser;
using ZeppelinForms.Drawing;
using ZF_SharedLib;

namespace ZF_Wasm;

static Task Main() => BrowserApp.RunAsync(
    new ExampleMainForm(),
    font: "/fonts/Inter-Regular.ttf",
    preload: ["/Assets/Laughing.png"]);