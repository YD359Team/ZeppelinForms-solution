namespace ZeppelinForms;

/// <summary>The application's jump list and its recent documents.</summary>
public partial class App
{
    /// <summary>The application's jump list: change it, then <see cref="JumpList.Apply"/>.</summary>
    public static JumpList JumpList { get; } = new();

    /// <summary>Tell the system a document was opened: it goes into the recent and the
    /// frequent documents of <see cref="JumpList"/>, and the system's own lists of recent
    /// files. Only a file of a type the application is registered to open is listed in its
    /// jump list. Does nothing where the platform keeps no such list.</summary>
    public static void AddRecentDocument(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (CurrentPlatform is IJumpListPlatform platform)
            platform.AddRecentDocument(Path.GetFullPath(path));
    }
}
