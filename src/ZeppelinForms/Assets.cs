using ZeppelinForms.Drawing.Imaging;

namespace ZeppelinForms;

/// <summary>Встроенные ресурсы фреймворка.</summary>
public static class Assets
{
    private const string IconResource = "ZeppelinForms.Resources.ZF.ico";

    private static Icon? _logo;

    /// <summary>Иконка ZeppelinForms. Читается один раз: ICO разбирается
    /// при первом обращении, дальше отдаётся тот же объект.</summary>
    public static Icon Logo => _logo ??= Icon.FromStream(
        typeof(Assets).Assembly.GetManifestResourceStream(IconResource)
            ?? throw new InvalidOperationException($"Ресурс {IconResource} не найден."));

    /// <summary>Логотип как изображение нужного размера.</summary>
    public static Image LogoImage(int size = 256) => Logo.ToImage(size, size);

    /// <summary>Каталог, откуда читаются ресурсы приложения.</summary>
    /// <remarks>
    /// Настраивается, потому что «рядом с исполняемым файлом» — допущение
    /// настольных платформ. В APK ресурсы лежат внутри архива и путём
    /// не адресуются: бэкенд копирует их в каталог приложения и ставит
    /// сюда его. То же понадобится там, где приложение упаковано
    /// в единый файл или запущено из песочницы.
    /// </remarks>
    public static string Root { get; set; } = Path.Combine(AppContext.BaseDirectory, "Assets");
}
