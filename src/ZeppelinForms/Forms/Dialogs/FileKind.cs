using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Forms.Dialogs;

/// <summary>What a row of the file dialog stands for: the kind picks its icon.</summary>
internal enum FileKind
{
    Folder,
    Image,
    Text,
    Archive,
    Audio,
    Video,
    Executable,
    Shortcut,
    Other,
}

/// <summary>
/// Tells a file's kind by its extension, and on Unix by its execute bit, and the
/// icon to show for each kind. The icons are outlines on a 24-unit grid; every path
/// starts with <c>M0 0M24 24</c>, two bare moves that make the grid its bounds, since
/// the renderers fit a path into its box by its bounds — without them a short music
/// note and a tall document would be scaled to different sizes.
/// </summary>
internal static class FileKinds
{
    private static readonly Dictionary<string, FileKind> ByExtension = Build();

    private static Dictionary<string, FileKind> Build()
    {
        var map = new Dictionary<string, FileKind>(StringComparer.OrdinalIgnoreCase);

        void Add(FileKind kind, params string[] extensions)
        {
            foreach (string extension in extensions)
                map[extension] = kind;
        }

        Add(FileKind.Image, "png", "jpg", "jpeg", "gif", "bmp", "webp", "svg", "ico", "tif", "tiff", "heic", "heif", "avif", "psd", "raw");

        // documents count as text: what a person reads, rather than what a program runs
        Add(FileKind.Text, "txt", "md", "log", "csv", "tsv", "json", "xml", "yaml", "yml", "ini", "cfg", "conf", "toml",
            "rtf", "pdf", "doc", "docx", "odt", "xls", "xlsx", "ods", "ppt", "pptx", "odp", "epub",
            "cs", "fs", "vb", "js", "ts", "py", "java", "kt", "go", "rs", "c", "h", "cpp", "hpp", "html", "htm", "css",
            "zfml", "zss", "sql");

        Add(FileKind.Archive, "zip", "rar", "7z", "tar", "gz", "tgz", "bz2", "xz", "zst", "lz", "lzma", "cab", "iso",
            "dmg", "jar", "nupkg", "deb", "rpm", "apk");

        Add(FileKind.Audio, "mp3", "wav", "flac", "ogg", "oga", "opus", "m4a", "aac", "wma", "aiff", "mid", "midi");

        Add(FileKind.Video, "mp4", "mkv", "avi", "mov", "wmv", "webm", "flv", "m4v", "mpg", "mpeg", "3gp");

        Add(FileKind.Executable, "exe", "msi", "bat", "cmd", "com", "ps1", "sh", "bash", "appimage", "run");

        // a Windows shortcut, an internet shortcut, a Linux launcher
        Add(FileKind.Shortcut, "lnk", "url", "desktop", "webloc");

        return map;
    }

    /// <summary>The kind its name tells, without touching the disk.</summary>
    public static FileKind FromName(string name)
    {
        string extension = Path.GetExtension(name);

        return extension.Length > 1 && ByExtension.TryGetValue(extension[1..], out FileKind kind)
            ? kind
            : FileKind.Other;
    }

    /// <summary>The kind of a file on disk: a symbolic link is a shortcut whatever it
    /// points to, and on Unix a file with the execute bit runs whatever its name.</summary>
    public static FileKind Of(string path)
    {
        FileKind kind = FromName(path);

        // a link or a mode the file system refuses to tell keeps the kind of the name
        try
        {
            var info = new FileInfo(path);

            if (info.LinkTarget is not null) return FileKind.Shortcut;

            if (kind == FileKind.Other && !OperatingSystem.IsWindows() &&
                (info.UnixFileMode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0)
                return FileKind.Executable;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return kind;
    }

    private const string Grid = "M0 0M24 24";

    // the sheet of paper the document kinds are drawn on: a folded corner at the top right
    private const string Sheet = "M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8zM14 2v6h6";

    /// <summary>The icon of a kind: path data, the stroke width in grid units
    /// (0 — filled) and the color. The colors are mid tones that read on both a light
    /// and a dark background, so the icons need no theme.</summary>
    public static (string PathData, float StrokeWidth, Color Color) Icon(FileKind kind) => kind switch
    {
        FileKind.Folder => (Grid + "M2 6a2 2 0 0 1 2-2h5l2 2h9a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2z",
            0f, new Color(255, 0xE8, 0xA8, 0x30)),

        FileKind.Image => (Grid + "M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z" +
            "M7 9a2 2 0 1 0 4 0a2 2 0 1 0-4 0M21 15l-5-5L5 21",
            1.8f, new Color(255, 0x2E, 0xA0, 0x5C)),

        FileKind.Text => (Grid + Sheet + "M8 13h8M8 17h8M8 9h2",
            1.8f, new Color(255, 0x4F, 0x86, 0xC6)),

        FileKind.Archive => (Grid + Sheet + "M10 3v1M12 5v1M10 7v1M12 9v1M10 11v1M10 14h2v4h-2z",
            1.8f, new Color(255, 0xB0, 0x7A, 0x3E)),

        FileKind.Audio => (Grid + "M9 18V5l12-2v13M3 18a3 3 0 1 0 6 0a3 3 0 1 0-6 0M15 16a3 3 0 1 0 6 0a3 3 0 1 0-6 0",
            1.8f, new Color(255, 0x9B, 0x5D, 0xE0)),

        FileKind.Video => (Grid + "M4 5h16a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2zM10 9l5 3-5 3z",
            1.8f, new Color(255, 0xE0, 0x55, 0x55)),

        FileKind.Executable => (Grid + "M4 4h16a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2zM6 9l3 3-3 3M12 15h5",
            1.8f, new Color(255, 0x1F, 0x9A, 0x9E)),

        FileKind.Shortcut => (Grid + "M15 3h6v6M10 14L21 3M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6",
            1.8f, new Color(255, 0x3B, 0x82, 0xF0)),

        _ => (Grid + Sheet,
            1.8f, new Color(255, 0x8A, 0x94, 0xA0)),
    };
}