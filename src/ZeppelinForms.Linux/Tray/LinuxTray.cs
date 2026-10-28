using ZeppelinForms.Forms;

namespace ZeppelinForms.Linux.Tray;

/// <summary>The tray icons of a Linux platform — X11 or Wayland alike, the tray is the
/// session bus's, not the display's: each icon a <see cref="StatusNotifierItem"/> with a
/// connection of its own, which the platform's loop reads next to the display.</summary>
internal sealed class LinuxTray
{
    private readonly List<StatusNotifierItem> _items = [];
    private bool? _hostPresent;

    /// <summary>A panel collects the icons on this session — looked at once, the first
    /// time it is asked.</summary>
    public bool IsHostPresent => _hostPresent ??= StatusNotifierItem.IsHostPresent();

    /// <summary>The icons.</summary>
    public IReadOnlyList<StatusNotifierItem> Items => _items;

    /// <summary>The desktop's notifications: on the session bus too, and polled with the icons.</summary>
    public LinuxNotifications Notifications { get; } = new();

    /// <summary>The desktop's file dialogs: the portal's, on the session bus too, and
    /// polled with the icons.</summary>
    public LinuxFileChooser FileChooser { get; } = new();

    /// <summary>The sockets the loop waits on for the session bus's calls and signals:
    /// each icon's, the notifications' and the file dialogs'. −1 for one that isn't open.</summary>
    public void CollectFileDescriptors(List<int> into)
    {
        foreach (StatusNotifierItem item in _items)
            into.Add(item.FileDescriptor);

        into.Add(Notifications.FileDescriptor);
        into.Add(FileChooser.FileDescriptor);
    }

    /// <summary>The icon on the bus; null without a session bus. Without a panel yet it
    /// waits on the bus and appears when one comes.</summary>
    public ITrayIconImpl? Create(TrayIcon owner)
    {
        StatusNotifierItem? item = StatusNotifierItem.TryCreate(owner, disposed => _items.Remove(disposed));

        if (item is not null) _items.Add(item);

        return item;
    }

    /// <summary>Answer what the panels asked, each icon on its own: a handler that
    /// threw doesn't keep the others from their calls. Without data it returns at once.</summary>
    public void Poll()
    {
        foreach (StatusNotifierItem item in _items.ToArray())
            App.RunGuarded(item.Poll);

        App.RunGuarded(Notifications.Poll);
        App.RunGuarded(FileChooser.Poll);
    }
}