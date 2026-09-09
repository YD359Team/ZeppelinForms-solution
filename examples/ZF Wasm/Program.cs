using ZeppelinForms;
using ZeppelinForms.Browser;
using ZeppelinForms.Drawing;
using ZF_SharedLib;

namespace ZF_Wasm;

public class Program
{
    static async Task Main()
    {
        BrowserPlatform browserPlatform = await BrowserPlatform.CreateAsync();

        // системных шрифтов в браузере нет: SKFontManager.Default ничего
        // не найдёт, поэтому шрифт берётся из виртуальной ФС по пути.
        // Начертания раздельные — при заданном FilePath вес и наклон
        // в SkiaFontCache не учитываются
        Font.Default = new Font("Inter", 14).WithFile("/fonts/Inter-Regular.ttf");

        App myApp = new(browserPlatform)
        {
            MainForm = new ExampleMainForm()
        };
        myApp.Run();
    }
}