namespace ZeppelinForms.Forms.Dialogs;

public sealed class FileDialogOptions
{
    public string? Title { get; set; }

    /// <summary>The folder to open in. Empty — the Documents folder.</summary>
    public string? InitialDirectory { get; set; }

    /// <summary>The suggested name. Meaningful only when saving.</summary>
    public string? FileName { get; set; }

    public List<FileFilter> Filters { get; init; } = [];

    public bool AllowMultiple { get; set; }
}