using System.Text.Json;
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

    /// <summary>
    /// Записать выбранное в виртуальную ФС. Вызывается из JS до того, как
    /// pickFiles вернёт управление, поэтому к моменту выхода из OpenAsync
    /// файлы уже на месте и читаются обычным File.OpenRead.
    /// </summary>
    /// <param name="json">Массив вида [{"name": "...", "data": "base64"}].</param>
    internal static void Save(string json)
    {
        Directory.CreateDirectory(UploadDirectory);

        // JsonDocument, а не десериализация в тип: обход отражения
        // переживает обрезку сборки, которую WASM включает по умолчанию
        using JsonDocument document = JsonDocument.Parse(json);

        foreach (JsonElement file in document.RootElement.EnumerateArray())
        {
            if (!file.TryGetProperty("name", out JsonElement name)) continue;
            if (!file.TryGetProperty("data", out JsonElement data)) continue;

            // только имя: браузер путей не отдаёт, но в имени может
            // оказаться разделитель — тогда запись ушла бы мимо /uploads
            string fileName = Path.GetFileName(name.GetString() ?? string.Empty);

            if (fileName.Length == 0) continue;

            File.WriteAllBytes(
                Path.Combine(UploadDirectory, fileName),
                Convert.FromBase64String(data.GetString() ?? string.Empty));
        }
    }

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