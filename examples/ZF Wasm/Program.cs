using ZeppelinForms.Browser;
using ZF_SharedLib;

namespace ZF_Wasm;

public class Program
{
    static Task Main() => BrowserApp.RunAsync(
        () => new ExampleMainForm(),
        font: "/fonts/Inter-Regular.ttf",
        preload: ["/Assets/Laughing.png"]);
}