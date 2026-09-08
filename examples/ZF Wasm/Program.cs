using ZeppelinForms;
using ZeppelinForms.Browser;
using ZF_SharedLib;

namespace ZF_Wasm;

public class Program
{
    static async Task<BrowserPlatform> Main()
    {
        BrowserPlatform browserPlatform = await BrowserPlatform.CreateAsync();
        App myApp = new(browserPlatform)
        {
            MainForm = new ExampleMainForm()
        };
        myApp.Run();
        return browserPlatform;
    }
}