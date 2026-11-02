using ZeppelinForms.Core.Collections;

namespace ZeppelinForms;

/// <summary>
/// The application's jump list: what the system offers on its taskbar button — the menu a
/// right click opens on Windows. Tasks that start the application again with arguments of
/// their own, categories of links, the recent and the frequent documents.
/// </summary>
/// <remarks>
/// <para>
/// One list for the application, not one per window: the system keeps it by the
/// application's id (<see cref="App.Id"/>) and shows it even when nothing of the
/// application runs, so it outlives the process. Change it, then <see cref="Apply"/>: the
/// list is handed to the system as a whole, the way the system takes it.
/// </para>
/// <para>
/// A task starts a new process of the application with its <see cref="JumpTask.Arguments"/>;
/// the application reads them from its command line, as it reads any. An application that
/// runs once hands them on to the running instance itself.
/// </para>
/// <para>
/// Windows has jump lists; elsewhere <see cref="Apply"/> returns false
/// (<see cref="PlatformCapabilities.JumpList"/>). The recent and the frequent documents are
/// the files the application registered as their opener and gave to
/// <see cref="App.AddRecentDocument"/>: the system lists no other.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// App.JumpList.Tasks.Add(new JumpTask("New document", "--new") { Description = "Start with an empty document" });
/// App.JumpList.Tasks.Add(new JumpTask("Open the last session", "--restore"));
/// App.JumpList.ShowRecent = true;
/// App.JumpList.Apply();
/// </code>
/// </example>
public sealed class JumpList
{
    internal JumpList() { }

    /// <summary>The tasks, at the bottom of the list under the system's own heading.</summary>
    public ZfCollection<JumpTask> Tasks { get; } = [];

    /// <summary>Categories of the application's own, above the tasks, each under its title.</summary>
    public ZfCollection<JumpCategory> Categories { get; } = [];

    /// <summary>The system's category of the documents opened last.</summary>
    public bool ShowRecent { get; set; }

    /// <summary>The system's category of the documents opened most.</summary>
    public bool ShowFrequent { get; set; }

    /// <summary>Hand the list to the system, in place of the one it had. True — the
    /// platform has jump lists and took it.</summary>
    /// <remarks>Call it on the UI thread, once the application is made: the platform is
    /// the one given to <see cref="App"/>.</remarks>
    public bool Apply() =>
        App.CurrentPlatform is IJumpListPlatform platform && platform.ApplyJumpList(this);

    /// <summary>Empty the list and hand the empty one to the system: the application's
    /// button offers nothing of the application's any more.</summary>
    public bool Clear()
    {
        Tasks.Clear();
        Categories.Clear();
        ShowRecent = false;
        ShowFrequent = false;

        return Apply();
    }
}

/// <summary>A link of a jump list: the application started again with
/// <see cref="Arguments"/> — or another program, by <see cref="ApplicationPath"/>.</summary>
public sealed class JumpTask
{
    public JumpTask(string title, string arguments = "")
    {
        Title = title;
        Arguments = arguments;
    }

    /// <summary>A line between the tasks around it.</summary>
    public static JumpTask Separator() => new(string.Empty) { IsSeparator = true };

    public string Title { get; set; }

    /// <summary>The command line the program is started with.</summary>
    public string Arguments { get; set; }

    /// <summary>The tooltip of the link.</summary>
    public string? Description { get; set; }

    /// <summary>The file of the icon — an .ico, or an .exe or a .dll with icons in it — and
    /// <see cref="IconIndex"/> in it. Null — the program's own icon.</summary>
    public string? IconPath { get; set; }

    public int IconIndex { get; set; }

    /// <summary>The program the link starts. Null — the running application: its executable,
    /// or the host with the application's assembly when it runs as <c>dotnet app.dll</c>.</summary>
    public string? ApplicationPath { get; set; }

    /// <summary>The folder the program starts in. Null — the system's choice.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>A line rather than a link: see <see cref="Separator"/>.</summary>
    public bool IsSeparator { get; private init; }

    public override string ToString() => IsSeparator ? "—" : Title;
}

/// <summary>A category of a jump list: links under a title of the application's.</summary>
public sealed class JumpCategory(string title)
{
    public string Title { get; set; } = title;

    public ZfCollection<JumpTask> Items { get; } = [];

    public override string ToString() => Title;
}

/// <summary>A platform with a jump list on the application's button.</summary>
internal interface IJumpListPlatform
{
    /// <summary>Put the list in place of the one the system had. False — it was refused.</summary>
    bool ApplyJumpList(JumpList list);

    /// <summary>Tell the system a document was opened, for its recent and frequent lists.</summary>
    void AddRecentDocument(string path);
}
