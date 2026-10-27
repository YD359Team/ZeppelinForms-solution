using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Dialogs;
using ZeppelinForms.Linux.DBus;

namespace ZeppelinForms.Linux.Tray;

/// <summary>
/// The desktop's file dialogs: the FileChooser of the XDG desktop portal,
/// <c>org.freedesktop.portal.Desktop</c> on the session bus. The portal shows the dialog of
/// the desktop — GTK's on GNOME, KDE's on Plasma — and from a Flatpak or a Snap the only one
/// that sees the user's files. X11 and Wayland alike.
/// </summary>
/// <remarks>
/// <para>
/// The call returns at once with a request object, and the answer comes later as its
/// Response signal. The connection is the platform's to poll, as the notifications' is
/// (see <see cref="LinuxTray"/>), so the answer wakes the loop like any input and the
/// task completes on the UI thread.
/// </para>
/// <para>
/// No portal on the bus, or a call it refused — null, and the framework's own dialog is
/// shown: a bare window manager without xdg-desktop-portal is a desktop too.
/// </para>
/// </remarks>
internal sealed class LinuxFileChooser
{
    private const string Service = "org.freedesktop.portal.Desktop";
    private const string ObjectPath = "/org/freedesktop/portal/desktop";
    private const string Interface = "org.freedesktop.portal.FileChooser";
    private const string RequestInterface = "org.freedesktop.portal.Request";

    /// <summary>The portal may be started by the bus for the first call: room for that.</summary>
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(5);

    private readonly Dictionary<string, TaskCompletionSource<string[]?>> _pending = new(StringComparer.Ordinal);

    private DBusConnection? _bus;
    private bool _tried;
    private bool? _available;
    private int _requests;

    /// <summary>A portal is on the bus, or the bus starts one when asked. Looked at once.</summary>
    public bool IsAvailable => _available ??= Probe();

    /// <summary>The connection's socket, for the loop's poll; −1 before the first dialog
    /// and once closed.</summary>
    public int FileDescriptor => _bus?.FileDescriptor ?? -1;

    /// <summary>A dialog is open: its answer is still to come.</summary>
    public bool IsWaiting => _pending.Count > 0;

    /// <summary>Show the dialog; the paths chosen, empty when cancelled. Null — no portal,
    /// or it refused: the framework's own dialog is to be shown.</summary>
    /// <param name="parent">The owner window, as the portal names windows:
    /// <c>x11:&lt;XID in hex&gt;</c>, or empty — the dialog is then a window of its own.</param>
    public Task<string[]?> ShowAsync(string parent, FileDialogOptions options, FileDialogMode mode)
    {
        if (!IsAvailable || Bus() is not { } bus) return Task.FromResult<string[]?>(null);

        string token = "zf" + (++_requests).ToString(System.Globalization.CultureInfo.InvariantCulture);

        // the request's path is known before the call — the portal makes it from the
        // sender and the token — so the answer can't come before it is waited for
        string expected = RequestPath(bus.UniqueName, token);

        var completion = new TaskCompletionSource<string[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[expected] = completion;

        bool save = mode == FileDialogMode.Save;

        DBusMessage? reply = bus.CallMethod(Service, ObjectPath, Interface, save ? "SaveFile" : "OpenFile", "ssa{sv}",
        [
            parent,
            options.Title ?? string.Empty,
            Options(options, mode, token),
        ]);

        if (reply is not { Type: DBusMessageType.MethodReturn, Body: [string handle, ..] })
        {
            _pending.Remove(expected);
            return Task.FromResult<string[]?>(null);
        }

        // portals before 0.9 made the path their own way: wait on the one they returned
        if (handle != expected && _pending.Remove(expected))
        {
            _pending[handle] = completion;
            bus.AddMatch(ResponseRule(handle));
        }

        return completion.Task;
    }

    /// <summary>Read what the portal sent: the answers. Without data it returns at once.</summary>
    public void Poll() => _bus?.ProcessIncoming();

    /// <summary>The options of OpenFile and SaveFile: an a{sv} dictionary.</summary>
    private static Dictionary<string, DBusVariant> Options(FileDialogOptions options, FileDialogMode mode, string token)
    {
        var result = new Dictionary<string, DBusVariant>(StringComparer.Ordinal)
        {
            ["handle_token"] = new("s", token),
            ["modal"] = new("b", true),
        };

        if (mode == FileDialogMode.Folder) result["directory"] = new("b", true);
        if (mode == FileDialogMode.Open && options.AllowMultiple) result["multiple"] = new("b", true);

        if (mode != FileDialogMode.Folder && options.Filters.Count > 0)
        {
            object[][] filters = [.. options.Filters.Select(Filter)];

            result["filters"] = new("a(sa(us))", filters);
            result["current_filter"] = new("(sa(us))", filters[0]);
        }

        if (mode == FileDialogMode.Save && !string.IsNullOrEmpty(options.FileName))
            result["current_name"] = new("s", Path.GetFileName(options.FileName));

        // the folder asked for, or the one of a file name given with its path
        string? folder = !string.IsNullOrEmpty(options.InitialDirectory)
            ? options.InitialDirectory
            : Path.GetDirectoryName(options.FileName);

        // a byte string with its terminating zero: a path needn't be UTF-8
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            result["current_folder"] = new("ay", ZeroTerminated(Path.GetFullPath(folder)));

        return result;
    }

    /// <summary>A filter as the portal takes it: ("Images", [(0, "*.png"), (0, "*.PNG")]) —
    /// 0 is a glob, and globs are case-sensitive in GTK 3, so both cases.</summary>
    private static object[] Filter(FileFilter filter)
    {
        var patterns = new List<object[]>();

        if (filter.Extensions.Length == 0)
            patterns.Add([0u, "*"]);

        foreach (string written in filter.Extensions)
        {
            string extension = written.TrimStart('*', '.');

            if (extension is "" or "*")
            {
                patterns.Add([0u, "*"]);
                continue;
            }

            patterns.Add([0u, "*." + extension.ToLowerInvariant()]);

            if (extension.ToUpperInvariant() != extension.ToLowerInvariant())
                patterns.Add([0u, "*." + extension.ToUpperInvariant()]);
        }

        return [filter.Description, patterns];
    }

    private static byte[] ZeroTerminated(string text) => [.. System.Text.Encoding.UTF8.GetBytes(text), 0];

    private void OnSignal(DBusMessage signal)
    {
        // Response(response, results): 0 — chosen, 1 — cancelled, 2 — ended otherwise
        if (signal is not { Interface: RequestInterface, Member: "Response", Path: { } path, Body: [uint response, ..] })
            return;

        if (!_pending.Remove(path, out TaskCompletionSource<string[]?>? completion)) return;

        completion.TrySetResult(response switch
        {
            0 => signal.Body is [_, IEnumerable<object?> results, ..] ? Paths(results) : [],
            1 => [],

            // the portal failed after the call: the framework's dialog
            _ => null,
        });
    }

    /// <summary>The "uris" of the results, as local paths. A URI that isn't a file —
    /// a phone's MTP storage, a network share not mounted — has no path and is left out.</summary>
    internal static string[] Paths(IEnumerable<object?> results)
    {
        foreach (object? entry in results)
        {
            if (entry is not object?[] { Length: 2 } pair || pair[0] is not "uris" || pair[1] is not DBusVariant uris)
                continue;

            if (uris.Unwrap() is not IEnumerable<object?> list) return [];

            var paths = new List<string>();

            foreach (object? item in list)
                if (item is string text && Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) && uri.IsFile)
                    paths.Add(uri.LocalPath);

            return [.. paths];
        }

        return [];
    }

    /// <summary>/org/freedesktop/portal/desktop/request/SENDER/TOKEN: the unique name
    /// without its ':' and with '_' for its dots.</summary>
    internal static string RequestPath(string? uniqueName, string token) =>
        $"{ObjectPath}/request/{(uniqueName ?? string.Empty).TrimStart(':').Replace('.', '_')}/{token}";

    private static string ResponseRule(string path) =>
        $"type='signal',interface='{RequestInterface}',member='Response',path='{path}'";

    private DBusConnection? Bus()
    {
        if (_bus is { IsOpen: true }) return _bus;

        // tried once: a session without a bus doesn't get a connection attempt per dialog
        if (_tried && _bus is null) return null;

        _tried = true;

        // a connection that broke takes its dialogs' answers with it
        foreach (TaskCompletionSource<string[]?> lost in _pending.Values.ToArray())
            lost.TrySetResult([]);

        _pending.Clear();

        _bus = DBusConnection.ConnectSession(CallTimeout);

        if (_bus is null) return null;

        // every request of this connection: their paths are under its name
        _bus.AddMatch(
            $"type='signal',interface='{RequestInterface}',member='Response'," +
            $"path_namespace='{ObjectPath}/request/{(_bus.UniqueName ?? string.Empty).TrimStart(':').Replace('.', '_')}'");

        _bus.Signal += OnSignal;

        return _bus;
    }

    /// <summary>A portal owns the name, or the bus can start one.</summary>
    private static bool Probe()
    {
        using DBusConnection? bus = DBusConnection.ConnectSession(TimeSpan.FromMilliseconds(500));

        if (bus is null) return false;

        if (bus.NameHasOwner(Service)) return true;

        DBusMessage? activatable = bus.CallMethod(
            "org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "ListActivatableNames", string.Empty, []);

        return activatable is { Type: DBusMessageType.MethodReturn, Body: [IEnumerable<object?> names, ..] } &&
            names.Contains(Service);
    }
}
