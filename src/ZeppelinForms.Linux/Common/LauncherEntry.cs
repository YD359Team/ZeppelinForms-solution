using ZeppelinForms.Forms;
using ZeppelinForms.Linux.DBus;

namespace ZeppelinForms.Linux.Common;

/// <summary>
/// The application's entry in the dock or the task manager: a progress and a count, sent as
/// the <c>com.canonical.Unity.LauncherEntry.Update</c> signal on the session bus. KDE's task
/// manager, Dash to Dock, Dash to Panel, Plank and Latte show it; GNOME's own dash doesn't.
/// X11 and Wayland alike — it is the session's, not the display's.
/// </summary>
/// <remarks>
/// <para>
/// It is the application's, not a window's: the dock finds the entry by the .desktop file
/// <c>application://{App.Id}.desktop</c>, so <see cref="App.Id"/> has to be the name of the
/// application's .desktop file, and every form of the application shows on the one entry.
/// The last form to set something wins.
/// </para>
/// <para>
/// Only a signal: nothing answers, and nothing has to be read but what the bus sends on
/// its own — a NameAcquired — which is drained after each signal so the socket doesn't fill.
/// The connection is made on the first update and kept for the process.
/// </para>
/// </remarks>
internal static class LauncherEntry
{
    private const string Interface = "com.canonical.Unity.LauncherEntry";

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(500);

    private static readonly object Locker = new();

    private static DBusConnection? s_bus;
    private static bool s_tried;

    private static TaskbarProgressState s_state;
    private static double s_value;
    private static int? s_count;

    /// <summary>A session bus is there to send the entry on. Whether a dock shows it is the dock's.</summary>
    public static bool IsAvailable =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")) ||
        (Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } runtime &&
         File.Exists(Path.Combine(runtime, "bus")));

    public static void SetProgress(TaskbarProgressState state, double value)
    {
        lock (Locker)
        {
            s_state = state;
            s_value = value;
            Send();
        }
    }

    public static void SetCount(int? count)
    {
        lock (Locker)
        {
            s_count = count;
            Send();
        }
    }

    private static void Send()
    {
        DBusConnection? bus = Bus();
        if (bus is null) return;

        // a dock draws no indeterminate bar: such a progress is shown as none rather than
        // as a bar stuck at a value that means nothing
        bool showProgress = s_state is TaskbarProgressState.Normal or TaskbarProgressState.Paused or TaskbarProgressState.Error;

        var properties = new Dictionary<string, DBusVariant>(StringComparer.Ordinal)
        {
            ["progress"] = new("d", showProgress ? s_value : 0d),
            ["progress-visible"] = new("b", showProgress),
            ["count"] = new("x", (long)(s_count ?? 0)),
            ["count-visible"] = new("b", s_count is not null),
        };

        bus.Emit(ObjectPath, Interface, "Update", "sa{sv}", [$"application://{App.Id}.desktop", properties]);

        // what the bus sent on its own, so it doesn't pile up unread
        bus.ProcessIncoming();
    }

    /// <summary>Any path of the application's: the docks go by the application's uri,
    /// not by the sender.</summary>
    private static string ObjectPath => $"/com/zeppelinforms/LauncherEntry/{Environment.ProcessId}";

    private static DBusConnection? Bus()
    {
        if (s_bus is { IsOpen: true }) return s_bus;

        // tried once: a session without a bus doesn't get a connection attempt per update
        if (s_tried && s_bus is null) return null;

        s_tried = true;
        s_bus = DBusConnection.ConnectSession(Timeout);

        return s_bus;
    }
}
