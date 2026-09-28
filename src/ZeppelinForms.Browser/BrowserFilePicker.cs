using System.Text.Json;
using ZeppelinForms.Forms.Dialogs;

namespace ZeppelinForms.Browser;

/// <summary>
/// File picking through &lt;input type=file&gt;. The browser gives the contents of
/// the picked files only as data, it has no path to the real file — so the files
/// are put into the virtual FS under /uploads, and from there all the code shared
/// with the desktop reads them with an ordinary File.OpenRead.
/// </summary>
public sealed class BrowserFilePicker : IFilePicker
{
    /// <summary>Where to put the picked files. There are no real paths in a browser,
    /// while the code above needs a path that opens.</summary>
    private const string UploadDirectory = "/uploads";

    public static void Register() => FilePicker.Current = new BrowserFilePicker();

    public async Task<string[]> OpenAsync(FileDialogOptions options)
    {
        string names = await Interop.PickFilesAsync(ToAccept(options), options.AllowMultiple);

        if (names.Length == 0) return [];

        // the names come separated by line breaks: a line break is impossible
        // in a file name, unlike a comma or a semicolon
        return [.. names.Split('\n').Select(name => $"{UploadDirectory}/{name}")];
    }

    /// <summary>Saving goes not through a dialog but through a download: the browser
    /// chooses the destination, and the application doesn't know it. Here it is only
    /// a path in the temporary FS — what is written there is handed out through Download.</summary>
    public Task<string?> SaveAsync(FileDialogOptions options)
    {
        string name = string.IsNullOrEmpty(options.FileName) ? "download" : options.FileName;

        Directory.CreateDirectory(UploadDirectory);

        return Task.FromResult<string?>($"{UploadDirectory}/{name}");
    }

    /// <summary>There is no folder picking in a browser, and nothing to work around it with.</summary>
    public Task<string?> SelectFolderAsync(FileDialogOptions options) =>
        Task.FromResult<string?>(null);

    /// <summary>
    /// Write the picked files into the virtual FS. Called from JS before pickFiles
    /// returns control, so by the time OpenAsync exits the files are already
    /// in place and read with an ordinary File.OpenRead.
    /// </summary>
    /// <param name="json">An array of the form [{"name": "...", "data": "base64"}].</param>
    internal static void Save(string json)
    {
        Directory.CreateDirectory(UploadDirectory);

        // JsonDocument rather than deserializing into a type: walking without
        // reflection survives the assembly trimming WASM turns on by default
        using JsonDocument document = JsonDocument.Parse(json);

        foreach (JsonElement file in document.RootElement.EnumerateArray())
        {
            if (!file.TryGetProperty("name", out JsonElement name)) continue;
            if (!file.TryGetProperty("data", out JsonElement data)) continue;

            // the name only: the browser gives no paths, but the name may contain
            // a separator — then the write would go past /uploads
            string fileName = Path.GetFileName(name.GetString() ?? string.Empty);

            if (fileName.Length == 0) continue;

            File.WriteAllBytes(
                Path.Combine(UploadDirectory, fileName),
                Convert.FromBase64String(data.GetString() ?? string.Empty));
        }
    }

    /// <summary>Hand a file to the user as a download. The only way to "save" from
    /// a browser: we have no writing to an arbitrary path.</summary>
    public static void Download(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);

        Interop.DownloadFile(Path.GetFileName(path), Convert.ToBase64String(bytes));
    }

    /// <summary>ZeppelinForms filters into the value of the accept attribute.</summary>
    private static string ToAccept(FileDialogOptions options)
    {
        if (options.Filters.Count == 0) return string.Empty;

        IEnumerable<string> patterns = options.Filters
            .SelectMany(filter => filter.Extensions)
            .Select(extension => extension.StartsWith('.') ? extension : $".{extension}");

        return string.Join(",", patterns);
    }
}