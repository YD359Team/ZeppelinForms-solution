using ZeppelinForms.Forms;
using ZeppelinForms.Linux.DBus;

namespace ZeppelinForms.Linux.Tray;

/// <summary>
/// The desktop's notifications: <c>org.freedesktop.Notifications</c> on the session bus —
/// GNOME, KDE, XFCE, Cinnamon, mako, dunst. X11 and Wayland alike.
/// </summary>
/// <remarks>
/// <para>
/// One connection of its own, made for the first notification and kept: its socket is
/// waited on by the platform's loop with the tray's (see <see cref="LinuxTray"/>), so a
/// click on a notification wakes the loop like any input, and <see cref="Notification.Clicked"/>
/// comes on the UI thread.
/// </para>
/// <para>
/// The click is the "default" action, which the servers invoke on a click of the
/// notification's body and don't show as a button. The application's .desktop file is
/// named by the "desktop-entry" hint — <see cref="App.Id"/> — and gives the notification
/// the application's name and icon where the server shows them.
/// </para>
/// </remarks>
internal sealed class LinuxNotifications
{
    private const string Service = "org.freedesktop.Notifications";
    private const string ObjectPath = "/org/freedesktop/Notifications";

    /// <summary>The server may be started by the bus for the first call: room for that.</summary>
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(2);

    private readonly Dictionary<uint, Notification> _shown = [];

    private DBusConnection? _bus;
    private bool _tried;
    private bool? _available;

    /// <summary>A server is on the bus, or the bus starts one when asked. Looked at once.</summary>
    public bool IsAvailable => _available ??= Probe();

    /// <summary>The connection's socket, for the loop's poll; −1 before the first
    /// notification and once closed.</summary>
    public int FileDescriptor => _bus?.FileDescriptor ?? -1;

    public bool Show(Notification notification)
    {
        DBusConnection? bus = Bus();
        if (bus is null) return false;

        string icon = notification.Kind switch
        {
            NotificationKind.Information => "dialog-information",
            NotificationKind.Warning => "dialog-warning",
            NotificationKind.Error => "dialog-error",

            // no icon of its own: the server takes the application's, by the desktop entry
            _ => string.Empty,
        };

        var hints = new Dictionary<string, DBusVariant>(StringComparer.Ordinal)
        {
            ["desktop-entry"] = new("s", App.Id),

            // low, normal, critical: an error stays until it is dismissed
            ["urgency"] = new("y", (byte)(notification.Kind == NotificationKind.Error ? 2 : 1)),
        };

        // Notify(app_name, replaces_id, app_icon, summary, body, actions, hints, expire_timeout)
        DBusMessage? reply = bus.CallMethod(Service, ObjectPath, Service, "Notify", "susssasa{sv}i",
        [
            App.DisplayName,
            0u,
            icon,
            notification.Title,
            notification.Text ?? string.Empty,
            new[] { "default", string.Empty },
            hints,
            -1,
        ]);

        if (reply is not { Type: DBusMessageType.MethodReturn, Body: [uint id, ..] }) return false;

        notification.MarkShown();
        _shown[id] = notification;

        return true;
    }

    /// <summary>Read what the server sent: the clicks and the closings. Without data it
    /// returns at once.</summary>
    public void Poll() => _bus?.ProcessIncoming();

    private void OnSignal(DBusMessage signal)
    {
        if (signal.Interface != Service) return;

        switch (signal)
        {
            // ActionInvoked(id, action_key): the body clicked — the "default" action
            case { Member: "ActionInvoked", Body: [uint id, "default", ..] }:
                if (_shown.TryGetValue(id, out Notification? clicked))
                    App.RunGuarded(clicked.RaiseClicked);
                break;

            // NotificationClosed(id, reason): expired, dismissed, closed by a call, other
            case { Member: "NotificationClosed", Body: [uint id, ..] }:
                if (_shown.Remove(id, out Notification? closed))
                    App.RunGuarded(closed.RaiseClosed);
                break;
        }
    }

    private DBusConnection? Bus()
    {
        if (_bus is { IsOpen: true }) return _bus;

        // tried once: a session without a bus doesn't get a connection attempt per notification
        if (_tried && _bus is null) return null;

        _tried = true;

        // a connection that broke takes its notifications with it: their clicks can't come
        foreach (Notification lost in _shown.Values.ToArray())
            App.RunGuarded(lost.RaiseClosed);

        _shown.Clear();

        _bus = DBusConnection.ConnectSession(CallTimeout);

        if (_bus is null) return null;

        _bus.AddMatch($"type='signal',interface='{Service}',path='{ObjectPath}'");
        _bus.Signal += OnSignal;

        return _bus;
    }

    /// <summary>A server owns the name, or the bus can start one.</summary>
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
