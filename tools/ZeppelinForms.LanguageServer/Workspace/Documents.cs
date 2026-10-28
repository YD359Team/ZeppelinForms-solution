namespace ZeppelinForms.LanguageServer.Workspace;

/// <summary>The documents the editor has open: their text as it is being typed, which is
/// not the text on the disk until a save.</summary>
internal sealed class Documents
{
    private readonly Dictionary<string, string> _open = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _sync = new();

    public void Set(string path, string text)
    {
        // a byte order mark some clients pass on is no character of the text: the offsets
        // of the problems would be one off, and the markup "invalid at the root"
        if (text.Length > 0 && text[0] == '\uFEFF') text = text[1..];

        lock (_sync) _open[path] = text;
    }

    public void Close(string path)
    {
        lock (_sync) _open.Remove(path);
    }

    public string? Get(string path)
    {
        lock (_sync) return _open.TryGetValue(path, out string? text) ? text : null;
    }

    /// <summary>The open text, or the file's.</summary>
    public string? Read(string path) => Get(path) ?? (File.Exists(path) ? File.ReadAllText(path) : null);

    /// <summary>The open documents of a folder and below it.</summary>
    public IReadOnlyList<(string Path, string Text)> Under(string directory)
    {
        string prefix = Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar;

        lock (_sync)
            return [.. _open.Where(pair => pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Select(pair => (pair.Key, pair.Value))];
    }

    /// <summary>file:///home/me/a.zfml → /home/me/a.zfml; file:///c%3A/x → c:\x.</summary>
    public static string PathOf(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed) || !parsed.IsFile) return uri;

        string path = parsed.LocalPath;

        // VS Code writes the drive as "c%3A": LocalPath gives "/c:/x" on some systems
        if (OperatingSystem.IsWindows() && path.Length > 2 && path[0] == '/' && path[2] == ':')
            path = path[1..];

        return Path.GetFullPath(path);
    }

    public static string UriOf(string path) => new Uri(path).AbsoluteUri;
}
