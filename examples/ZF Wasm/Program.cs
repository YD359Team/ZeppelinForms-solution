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
            // скачиваем до создания форм: дальше всё читается синхронно
            await BrowserPlatform.PreloadAsync(
                "/fonts/Inter-Regular.ttf",
                "/Assets/Laughing.png");

            BrowserPlatform browserPlatform = BrowserPlatform.Create();

            // системных шрифтов в браузере нет: SKFontManager.Default ничего
            // не найдёт, поэтому шрифт берётся из виртуальной ФС по пути
            Font.Default = new Font("Inter", 14).WithFile("/fonts/Inter-Regular.ttf");

            App myApp = new(browserPlatform)
            {
                MainForm = new ExampleMainForm()
            };

            myApp.Run();

            Console.WriteLine($"ZeppelinForms запущен, шрифт на месте: {File.Exists("/fonts/Inter-Regular.ttf")}");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Запуск не удался: {exception}");
            throw;
        }
    }
}