using Android.App;
using Android.Content.Res;
using ZeppelinForms.Forms;

// ZeppelinForms.Assets и Activity.Assets (AssetManager) — разные вещи
// с одним именем, и внутри этого неймспейса Assets разрешается в первое
using ZfAssets = ZeppelinForms.Assets;

namespace ZeppelinForms.Android;

/// <summary>
/// Запуск приложения на Android одной строкой. Делает то, что иначе
/// пришлось бы повторять в каждой активности: раскладывает ресурсы APK
/// по файловой системе, создаёт платформу и поднимает App.
/// </summary>
public static class AndroidApp
{
    /// <param name="mainForm">Именно фабрика, а не готовая форма: конструктор
    /// формы уже меряет текст, а измеритель появляется только после создания
    /// платформы.</param>
    /// <param name="assets">Пути ресурсов относительно папки Assets — те же,
    /// что уходят в Image.LoadAsset. Шрифт указывать не нужно: системные
    /// шрифты на Android есть, и Skia найдёт их сама — в отличие
    /// от браузера, где своих шрифтов нет вовсе.</param>
    /// <returns>Созданная платформа. Через неё приложение подписывается
    /// на жизненный цикл: Paused, Resumed и Saving — последняя возможность
    /// сохранить состояние перед тем, как систему могут убить процесс.</returns>
    public static AndroidPlatform Run(Activity activity, Func<Form> mainForm, IEnumerable<string>? assets = null)
    { 
        string files = activity.FilesDir?.AbsolutePath
            ?? throw new InvalidOperationException("FilesDir недоступен.");

        ZfAssets.Root = Path.Combine(files, "Assets");

        // до создания платформы: она регистрирует измеритель текста,
        // а форма полезет за ресурсами уже в конструкторе
        if (assets is not null)
            Unpack(activity, assets);

        AndroidPlatform platform = AndroidPlatform.Create(activity);

        App app = new(platform) { MainForm = mainForm() };
        app.Run();

        return platform;
    }

    private static void Unpack(Activity activity, IEnumerable<string> names)
    {
        AssetManager manager = activity.Assets
            ?? throw new InvalidOperationException("AssetManager недоступен.");

        Directory.CreateDirectory(ZfAssets.Root);

        foreach (string name in names)
        {
            string target = Path.Combine(ZfAssets.Root, name);

            if (Path.GetDirectoryName(target) is { Length: > 0 } directory)
                Directory.CreateDirectory(directory);

            // имя без префикса: AndroidAssetsPrefix (по умолчанию "Assets")
            // срезается при упаковке, и внутри APK ресурс лежит в корне
            // assets под тем же относительным именем, которое ждёт LoadAsset
            using Stream source = OpenAsset(manager, name);
            using FileStream destination = File.Create(target);

            source.CopyTo(destination);
        }
    }

    /// <summary>Открыть ресурс APK, а при неудаче — сказать, что там есть
    /// на самом деле. Раскладка внутри APK зависит от свойств сборки,
    /// и голый FileNotFoundException про неё не говорит ничего.</summary>
    private static Stream OpenAsset(AssetManager manager, string name)
    {
        try
        {
            return manager.Open(name);
        }
        catch (Java.IO.FileNotFoundException)
        {
            string[] actual = manager.List(string.Empty) ?? [];

            throw new FileNotFoundException(
                $"Ресурс \"{name}\" не найден в APK. В корне assets лежит: " +
                $"{(actual.Length == 0 ? "ничего" : string.Join(", ", actual))}.");
        }
    }
}