namespace ZeppelinForms.Forms.Dialogs;

public static class FileDialog
{
    public static string? OpenFile(Form owner, FileDialogOptions? options = null) =>
        Open(owner, options, multiple: false) is [string single, ..] ? single : null;

    public static string[] OpenFiles(Form owner, FileDialogOptions? options = null) =>
        Open(owner, options, multiple: true);

    public static string? SaveFile(Form owner, FileDialogOptions? options = null)
    {
        var dialog = new FileDialogForm(options ?? new FileDialogOptions(), FileDialogMode.Save);

        DialogResult<string[]> result = dialog.ShowDialog<string[]>(owner);

        return result.IsAccepted && result.Value is [string single, ..] ? single : null;
    }

    public static string? SelectFolder(Form owner, FileDialogOptions? options = null)
    {
        var dialog = new FileDialogForm(options ?? new FileDialogOptions(), FileDialogMode.Folder);

        DialogResult<string[]> result = dialog.ShowDialog<string[]>(owner);

        return result.IsAccepted && result.Value is [string single, ..] ? single : null;
    }

    private static string[] Open(Form owner, FileDialogOptions? options, bool multiple)
    {
        FileDialogOptions settings = options ?? new FileDialogOptions();
        settings.AllowMultiple = multiple;

        var dialog = new FileDialogForm(settings, FileDialogMode.Open);

        DialogResult<string[]> result = dialog.ShowDialog<string[]>(owner);

        return result.IsAccepted ? result.Value : [];
    }
}
