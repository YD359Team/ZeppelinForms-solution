using System.Reflection;
using System.Text;

namespace ZeppelinForms;

/// <summary>Who the application is: its id for the system, its name for people, its version.</summary>
/// <remarks>
/// <para>
/// Each is read from the entry assembly until set, so a project that keeps its version in
/// one place — <c>&lt;Version&gt;</c> of the project file or of Directory.Build.props — has
/// it in <see cref="Version"/> without writing it again.
/// </para>
/// <para>
/// Set them before <see cref="Run"/>: the platforms hand the id to the system as the windows
/// are made — the window class on X11, the app_id on Wayland, the application user model id
/// on Windows — and a window made before keeps the one it was given.
/// </para>
/// </remarks>
public partial class App
{
    private static string? s_id;
    private static string? s_displayName;
    private static string? s_version;

    /// <summary>The application's id for the system: what it groups the windows by and
    /// finds the application's .desktop file with on Linux — the docks' progress and badge
    /// go to <c>application://{Id}.desktop</c> — and the application user model id on Windows.
    /// Reverse-DNS by convention: <c>com.example.Orders</c>.</summary>
    /// <remarks>
    /// <para>
    /// Until set, the entry assembly's name with what an id can't hold — spaces, mostly —
    /// replaced by '-': "ZF Linux" becomes "ZF-Linux".
    /// </para>
    /// <para>
    /// Windows gets it only when it is set: the system groups a running window with the
    /// taskbar button pinned from its shortcut by this id, and a shortcut made without
    /// one would no longer find a window that suddenly had it.
    /// </para>
    /// </remarks>
    public static string Id
    {
        get => s_id ??= SanitizeId(EntryName() ?? "ZeppelinForms");
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            s_id = SanitizeId(value);
            IsIdSet = true;
        }
    }

    /// <summary>The id was set by the application, not derived from the assembly.</summary>
    internal static bool IsIdSet { get; private set; }

    /// <summary>The application's name for people: for an About box, and for the system
    /// where it shows the application rather than a window. Until set, the entry
    /// assembly's product — <c>&lt;Product&gt;</c>, which the SDK fills with the assembly's
    /// name — or its title.</summary>
    public static string DisplayName
    {
        get => s_displayName ??=
            EntryAttribute<AssemblyProductAttribute>()?.Product
            ?? EntryAttribute<AssemblyTitleAttribute>()?.Title
            ?? EntryName()
            ?? "ZeppelinForms";
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            s_displayName = value;
        }
    }

    /// <summary>The application's version: the entry assembly's informational version —
    /// <c>&lt;Version&gt;</c> of the project, a prerelease suffix included — without the
    /// "+commit" the SDK appends. "0.19.0-preview.1", not "0.19.0.0".</summary>
    public static string Version
    {
        get => s_version ??= EntryVersion();
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            s_version = value;
        }
    }

    /// <summary>Back to what the entry assembly says. For tests, which run many
    /// applications in one process.</summary>
    internal static void ResetIdentity()
    {
        s_id = null;
        s_displayName = null;
        s_version = null;
        IsIdSet = false;
    }

    private static string? EntryName() => Assembly.GetEntryAssembly()?.GetName().Name;

    private static T? EntryAttribute<T>() where T : Attribute =>
        Assembly.GetEntryAssembly()?.GetCustomAttribute<T>();

    private static string EntryVersion()
    {
        if (EntryAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion is { Length: > 0 } informational)
        {
            // the SDK appends the commit the build came from: "1.2.0+5d4c3b2a…"
            int plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus > 0 ? informational[..plus] : informational;
        }

        return Assembly.GetEntryAssembly()?.GetName().Version is { } version
            ? version.ToString(3)
            : "0.0.0";
    }

    /// <summary>An id the systems take: letters, digits, '.', '-' and '_', at most 128 —
    /// the limit of an application user model id; anything else becomes '-'.</summary>
    internal static string SanitizeId(string id)
    {
        var result = new StringBuilder(Math.Min(id.Length, 128));

        foreach (char c in id.Trim())
        {
            if (result.Length == 128) break;

            result.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '-');
        }

        return result.Length > 0 ? result.ToString() : "ZeppelinForms";
    }
}
