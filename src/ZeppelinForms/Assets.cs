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
}
