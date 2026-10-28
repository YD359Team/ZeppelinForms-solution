using ZeppelinForms.Forms;
using ZeppelinForms.Linux.Tray;

namespace ZeppelinForms.Linux;

/// <summary>The tray: StatusNotifierItems on the session bus, see <see cref="LinuxTray"/>.
/// Their sockets are waited on by the loop's select next to the X connection, so a
/// click on the icon wakes the loop like any input.</summary>
public sealed partial class X11Platform : ITrayPlatform, INotificationPlatform
{
    private readonly LinuxTray _tray = new();

    private readonly List<int> _trayDescriptors = [];

    ITrayIconImpl? ITrayPlatform.CreateTrayIcon(TrayIcon owner) => _tray.Create(owner);

    bool INotificationPlatform.ShowNotification(Notification notification) => _tray.Notifications.Show(notification);

    /// <summary>A notification server on the session bus.</summary>
    private PlatformCapabilities NotificationCapability =>
        _tray.Notifications.IsAvailable ? PlatformCapabilities.Notifications : PlatformCapabilities.None;

    /// <summary>The tray's capability: a panel to show the icons on.</summary>
    private PlatformCapabilities TrayCapability =>
        _tray.IsHostPresent ? PlatformCapabilities.SystemTray : PlatformCapabilities.None;

    /// <summary>Add the icons' and the notifications' sockets to the select set; returns
    /// the new nfds. As with the portal's, a socket past the 1024 of fd_set is still read
    /// on every turn.</summary>
    private int WatchTray(ref X11.FdSet readSet, int nfds)
    {
        _trayDescriptors.Clear();
        _tray.CollectFileDescriptors(_trayDescriptors);

        foreach (int fd in _trayDescriptors)
        {
            if (fd is < 0 or >= 1024) continue;

            readSet.Set(fd);
            nfds = Math.Max(nfds, fd + 1);
        }

        return nfds;
    }
}