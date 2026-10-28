namespace ZeppelinForms.Forms.Dialogs;

/// <summary>
/// A file the user picked: one to read from an open dialog, or one to write from a save
/// dialog — as a stream, wherever it lives. The way to files the same on every platform,
/// the browser and Android included, where a picked file has no path.
/// </summary>
/// <remarks>
/// <para>
/// On the desktop it is the file at <see cref="Path"/>. In the browser a picked file is
/// what the page was given — no path — and a file to save is written by the browser's
/// own save dialog, where it has one, or handed to the user as a download: either way
/// when the stream from <see cref="OpenWriteAsync"/> is disposed — <c>await using</c> it.
/// </para>
/// <para>
/// <see cref="FileDialog.PickFileAsync"/>, <see cref="FileDialog.PickFilesAsync"/> and
/// <see cref="FileDialog.PickSaveFileAsync"/> give them.
/// </para>
/// </remarks>
public abstract class PickedFile
{
    protected PickedFile(string name, string? path)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        Name = name;
        Path = path;
    }

    /// <summary>The file's name with its extension: "orders.csv".</summary>
    public string Name { get; }

    /// <summary>The file's full path where it has one — on the desktop; null in the
    /// browser and on Android, where the stream is the only way to it.</summary>
    public string? Path { get; }

    /// <summary>The file's content to read.</summary>
    public abstract Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default);

    /// <summary>A stream to write the file's new content to, from its start; what was there
    /// goes. Dispose it to finish the file — <c>await using</c> — where the writing happens
    /// on disposal: the browser's.</summary>
    public abstract Task<Stream> OpenWriteAsync(CancellationToken cancellationToken = default);

    /// <summary>The file at a path, as a picked file: for a path the application got
    /// another way — a list of recent files, a command line.</summary>
    public static PickedFile FromPath(string path) => new LocalPickedFile(path);

    public override string ToString() => Path ?? Name;
}

/// <summary>A file of the local file system.</summary>
internal sealed class LocalPickedFile(string path)
    : PickedFile(System.IO.Path.GetFileName(path) is { Length: > 0 } name ? name : path, System.IO.Path.GetFullPath(path))
{
    public override Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream>(new FileStream(Path!, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true));

    public override Task<Stream> OpenWriteAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream>(new FileStream(Path!, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true));
}

/// <summary>A platform's own file dialogs — the system's: IFileDialog on Windows, the
/// desktop portal on Linux. Null from either — none now, and the framework's own dialog
/// is shown instead.</summary>
internal interface INativeFileDialogs
{
    /// <summary>The dialog, modal, until the user closes it: the paths chosen, empty when
    /// cancelled. Null — no dialog that can be waited for synchronously here.</summary>
    string[]? Show(Form owner, FileDialogOptions options, FileDialogMode mode);

    /// <summary>The dialog, without blocking: the paths chosen, empty when cancelled.
    /// Null — no system dialog here now.</summary>
    Task<string[]?> ShowAsync(Form owner, FileDialogOptions options, FileDialogMode mode);
}
