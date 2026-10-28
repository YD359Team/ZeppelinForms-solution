namespace ZeppelinForms.Forms.Dialogs;

/// <summary>
/// The system file picker. Where it exists, FileDialog uses it instead of
/// its own browser: in the browser the application has no local file system,
/// and walking its folders makes no sense.
/// </summary>
public interface IFilePicker
{
    /// <summary>The selected paths. An empty array — cancelled.</summary>
    Task<string[]> OpenAsync(FileDialogOptions options);

    /// <summary>The path to save to, or null if cancelled.</summary>
    Task<string?> SaveAsync(FileDialogOptions options);

    /// <summary>The selected folder, or null. Not supported everywhere —
    /// the browser has no folder picking as such.</summary>
    Task<string?> SelectFolderAsync(FileDialogOptions options);

    /// <summary>The picked files as <see cref="PickedFile"/>s. By default the paths of
    /// <see cref="OpenAsync"/>; a picker whose files are not on a disk gives its own.</summary>
    async Task<PickedFile[]> PickOpenAsync(FileDialogOptions options) =>
        [.. (await OpenAsync(options)).Select(PickedFile.FromPath)];

    /// <summary>The file to save as a <see cref="PickedFile"/>, or null. By default the path
    /// of <see cref="SaveAsync"/>; a picker that saves another way — a browser's download —
    /// gives its own.</summary>
    async Task<PickedFile?> PickSaveAsync(FileDialogOptions options) =>
        await SaveAsync(options) is { } path ? PickedFile.FromPath(path) : null;
}

public static class FilePicker
{
    /// <summary>null — we make do with the built-in browser form.
    /// Set by the platform at startup.</summary>
    public static IFilePicker? Current { get; set; }
}