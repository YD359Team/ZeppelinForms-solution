using ZeppelinForms.Forms.Dialogs;

namespace ZeppelinForms.Browser;

/// <summary>
/// Выбор файлов через &lt;input type=file&gt;. Содержимое выбранного браузер
/// отдаёт только как данные, пути к настоящему файлу у него нет — поэтому
/// файлы кладутся в виртуальную ФС под /uploads, и дальше весь общий
/// с десктопом код читает их обычным File.OpenRead.
/// </summary>
public sealed class BrowserFilePicker : IFilePicker
{
    /// <summary>Куда складывать выбранное. Настоящих путей в браузере нет,
    /// а коду выше нужен путь, который открывается.</summary>
    private const string UploadDirectory = "/uploads";

    public static void Register() => FilePicker.Current = new BrowserFilePicker();

    public async Task<string[]> OpenAsync(FileDialogOptions options)
    {
        string names = await Interop.PickFilesAsync(ToAccept(options), options.AllowMultiple);

        if (names.Length == 0) return [];

        // имена возвращаются через перевод строки: в имени файла он невозможен,
        // в отличие от запятой или точки с запятой
        return [.. names.Split('\n').Select(name => $"{UploadDirectory}/{name}")];
    }

    /// <summary>Сохранение идёт не через диалог, а через скачивание: место
    /// назначения выбирает браузер, приложению оно неизвестно. Здесь только
    /// путь во временной ФС — записанное туда отдаётся через Download.</summary>
    public Task<string?> SaveAsync(FileDialogOptions options)
    {
        string name = string.IsNullOrEmpty(options.FileName) ? "download" : options.FileName;

        Directory.CreateDirectory(UploadDirectory);

        return Task.FromResult<string?>($"{UploadDirectory}/{name}");
    }

    /// <summary>Выбора папки в браузере нет и обойти это нечем.</summary>
    public Task<string?> SelectFolderAsync(FileDialogOptions options) =>
        Task.FromResult<string?>(null);

    /// <summary>Отдать файл пользователю как скачивание. Единственный способ
    /// «сохранить» из браузера: записи по произвольному пути у нас нет.</summary>
    public static void Download(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);

        Interop.DownloadFile(Path.GetFileName(path), Convert.ToBase64String(bytes));
    }

    /// <summary>Фильтры ZeppelinForms в значение атрибута accept.</summary>
    private static string ToAccept(FileDialogOptions options)
    {
        if (options.Filters.Count == 0) return string.Empty;

        IEnumerable<string> patterns = options.Filters
            .SelectMany(filter => filter.Extensions)
            .Select(extension => extension.StartsWith('.') ? extension : $".{extension}");

        return string.Join(",", patterns);
    }
}