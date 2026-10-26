using ZeppelinForms.Forms;
using ZeppelinForms.Linux.Tray;

namespace ZeppelinForms.Linux;

/// <summary>The tray: StatusNotifierItems on the session bus, see <see cref="LinuxTray"/>.
/// Their sockets are waited on by the loop's select next to the X connection, so a
/// click on the icon wakes the loop like any input.</summary>
public sealed partial class X11Platform : ITrayPlatform
{
    private readonly LinuxTray _tray = new();

    ITrayIconImpl? ITrayPlatform.CreateTrayIcon(TrayIcon owner) => _tray.Create(owner);

    /// <summary>The tray's capability: a panel to show the icons on.</summary>
    private PlatformCapabilities TrayCapability =>
        _tray.IsHostPresent ? PlatformCapabilities.SystemTray : PlatformCapabilities.None;

    /// <summary>Add the icons' sockets to the select set; returns the new nfds. As with
    /// the portal's, a socket past the 1024 of fd_set is still read on every turn.</summary>
    private int WatchTray(ref X11.FdSet readSet, int nfds)
    {
        foreach (StatusNotifierItem item in _tray.Items)
        {
            int fd = item.FileDescriptor;

            if (fd is < 0 or >= 1024) continue;

            readSet.Set(fd);
            nfds = Math.Max(nfds, fd + 1);
        }

        return nfds;
    }
}