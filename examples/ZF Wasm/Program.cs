using ZeppelinForms;
using ZeppelinForms.Browser;
using ZeppelinForms.Drawing;
using ZF_SharedLib;

namespace ZF_Wasm;

public class Program
{
    static async Task Main()
    {
        // без перехвата сбой в инициализации виден только в Console браузера,
        // а страница остаётся белой и не намекает, что вообще случилось
        try
        {
            BrowserPlatform browserPlatform = await BrowserPlatform.CreateAsync();

            // системных шрифтов в браузере нет: SKFontManager.Default ничего
            // не найдёт, поэтому шрифт берётся из виртуальной ФС по пути.
            // Начертания раздельные — при заданном FilePath вес и наклон
            // в SkiaFontCache не учитываются
            Font.Default = new Font("Inter", 14).WithFile("/fonts/Inter-Regular.ttf");

            if (!File.Exists("/fonts/Inter-Regular.ttf"))
                Console.WriteLine("Шрифт не найден в виртуальной ФС — проверь WasmFilesToIncludeInFileSystem");

            App myApp = new(browserPlatform)
            {
                MainForm = new ExampleMainForm()
            };

            myApp.Run();

            Console.WriteLine("ZeppelinForms запущен");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Запуск не удался: {exception}");
            throw;
        }
    }
}