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

    /// <summary>The system's own dialog where the platform has one: IFileDialog on Windows,
    /// the desktop portal on Linux — GNOME's, KDE's, whatever the desktop uses. True by
    /// default; false — the framework's own dialog, the same everywhere. A platform without
    /// a system dialog shows the framework's either way; the browser always has its own.</summary>
    /// <remarks>On Linux the portal answers asynchronously: the synchronous
    /// <see cref="FileDialog.OpenFile"/> and its kin show the framework's dialog there.</remarks>
    public bool UseNative { get; set; } = true;
}