using ZeppelinForms.Forms;
using ZeppelinForms.Linux.Tray;

namespace ZeppelinForms.Linux.Wayland;

/// <summary>The tray: StatusNotifierItems on the session bus, see <see cref="LinuxTray"/>.
/// Wayland itself has no tray — the panel is a client like any other, and the icons
/// reach it over the bus. Their sockets are polled with the display's.</summary>
public sealed partial class WaylandPlatform : ITrayPlatform
{
    private readonly LinuxTray _tray = new();

    ITrayIconImpl? ITrayPlatform.CreateTrayIcon(TrayIcon owner) => _tray.Create(owner);

    /// <summary>The tray's capability: a panel to show the icons on.</summary>
    private PlatformCapabilities TrayCapability =>
        _tray.IsHostPresent ? PlatformCapabilities.SystemTray : PlatformCapabilities.None;
}