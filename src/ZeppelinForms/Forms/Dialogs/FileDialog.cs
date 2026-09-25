namespace ZeppelinForms.Forms.Dialogs;

public static class FileDialog
{
    public static string? OpenFile(Form owner, FileDialogOptions? options = null) =>
        First(Open(owner, options, multiple: false));

    public static async Task<string?> OpenFileAsync(Form owner, FileDialogOptions? options = null) =>
        First(await OpenAsync(owner, options, multiple: false));

    public static string[] OpenFiles(Form owner, FileDialogOptions? options = null) =>
        Open(owner, options, multiple: true);

    public static Task<string[]> OpenFilesAsync(Form owner, FileDialogOptions? options = null) =>
        OpenAsync(owner, options, multiple: true);

    public static string? SaveFile(Form owner, FileDialogOptions? options = null) =>
        First(Run(owner, options, FileDialogMode.Save));

    public static async Task<string?> SaveFileAsync(Form owner, FileDialogOptions? options = null) =>
        First(await RunAsync(owner, options, FileDialogMode.Save));

    public static string? SelectFolder(Form owner, FileDialogOptions? options = null) =>
        First(Run(owner, options, FileDialogMode.Folder));

    public static async Task<string?> SelectFolderAsync(Form owner, FileDialogOptions? options = null) =>
        First(await RunAsync(owner, options, FileDialogMode.Folder));

    private static string[] Open(Form owner, FileDialogOptions? options, bool multiple) =>
        Run(owner, WithMultiple(options, multiple), FileDialogMode.Open);

    private static Task<string[]> OpenAsync(Form owner, FileDialogOptions? options, bool multiple) =>
        RunAsync(owner, WithMultiple(options, multiple), FileDialogMode.Open);

    private static string[] Run(Form owner, FileDialogOptions? options, FileDialogMode mode)
    {
        // the synchronous path does not fit the system picker: both in the browser
        // and in system dialogs the choice arrives through a callback
        if (FilePicker.Current is not null)
            throw new NotSupportedException(
                "File picking on this platform is asynchronous only: use OpenFileAsync, " +
                "OpenFilesAsync, SaveFileAsync or SelectFolderAsync.");

        var dialog = new FileDialogForm(options ?? new FileDialogOptions(), mode);

        return Unwrap(dialog.ShowDialog<string[]>(owner));
    }

    private static async Task<string[]> RunAsync(Form owner, FileDialogOptions? options, FileDialogMode mode)
    {
        FileDialogOptions settings = options ?? new FileDialogOptions();

        // the system picker, if the platform provides one
        if (FilePicker.Current is { } picker)
            return await PickAsync(picker, settings, mode);

        var dialog = new FileDialogForm(settings, mode);

        return Unwrap(await dialog.ShowDialogAsync<string[]>(owner));
    }

    private static async Task<string[]> PickAsync(
        IFilePicker picker,
        FileDialogOptions options,
        FileDialogMode mode)
    {
        return mode switch
        {
            FileDialogMode.Open => await picker.OpenAsync(options),
            FileDialogMode.Save => Single(await picker.SaveAsync(options)),
            _ => Single(await picker.SelectFolderAsync(options)),
        };

        static string[] Single(string? path) => path is null ? [] : [path];
    }

    // a copy rather than the caller's object: the same options are often kept
    // in a field and reused, and OpenFiles followed by OpenFile would otherwise
    // leave AllowMultiple in whatever state the last call put it
    private static FileDialogOptions WithMultiple(FileDialogOptions? options, bool multiple) => new()
    {
        Title = options?.Title,
        InitialDirectory = options?.InitialDirectory,
        FileName = options?.FileName,
        Filters = options?.Filters ?? [],
        AllowMultiple = multiple,
    };

    // an empty array instead of null: "cancelled" and "nothing selected"
    // are the same thing to the caller
    private static string[] Unwrap(DialogResult<string[]> result) =>
        result.IsAccepted && result.Value is { } files ? files : [];

    private static string? First(string[] files) => files is [string single, ..] ? single : null;
}