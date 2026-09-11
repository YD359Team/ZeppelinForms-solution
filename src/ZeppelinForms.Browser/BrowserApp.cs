using ZeppelinForms.Drawing;
using ZeppelinForms.Forms;

namespace ZeppelinForms.Browser;

/// <summary>
/// Запуск приложения в браузере одной строкой. Собирает вместе то, что
/// иначе приходится повторять в каждом Program.cs: скачивание ресурсов
/// в файловую систему, создание платформы, подстановку шрифта и перехват
/// сбоя инициализации.
/// </summary>
public static class BrowserApp
{
    /// <param name="font">Путь к файлу шрифта — он же адрес на сервере.
    /// null оставляет Font.Default как есть, и текст будет нарисован
    /// запасным начертанием: своих шрифтов у браузера для Skia нет.</param>
    /// <param name="preload">Остальные файлы, которые понадобятся синхронно:
    /// картинки для Image.LoadAsset и прочее с диска.</param>
    /// <param name="fontFamily">Имя семейства. По умолчанию берётся
    /// из имени файла — "Inter-Regular.ttf" даёт "Inter".</param>
    public static async Task RunAsync(
        Form mainForm,
        string? font = null,
        IEnumerable<string>? preload = null,
        string? fontFamily = null,
        float fontSize = 14f,
        string canvasId = "zf-canvas")
    {
        try
        {
            List<string> files = [];

            if (font is not null)
                files.Add(font);

            if (preload is not null)
                files.AddRange(preload);

            // скачиваем до создания платформы: она регистрирует измеритель
            // текста, а тот при первом же обращении полезет за шрифтом
            if (files.Count > 0)
                await BrowserPlatform.PreloadAsync([.. files]);

            BrowserPlatform platform = BrowserPlatform.Create(canvasId);

            if (font is not null)
            {
                Font.Default = new Font(fontFamily ?? FamilyFromPath(font), fontSize)
                    .WithFile(font);
            }

            App app = new(platform) { MainForm = mainForm };
            app.Run();
        }
        catch (Exception exception)
        {
            // без этого сбой инициализации виден только в Console браузера,
            // а страница остаётся белой и ни на что не намекает
            Console.WriteLine($"ZeppelinForms: запуск не удался. {exception}");
            throw;
        }
    }

    /// <summary>"/fonts/Inter-Regular.ttf" -> "Inter". Имя семейства нужно
    /// только для сравнения начертаний внутри кэша: сам шрифт берётся
    /// по пути, и системного поиска не происходит.</summary>
    private static string FamilyFromPath(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        int dash = name.IndexOf('-');

        return dash > 0 ? name[..dash] : name;
    }
}