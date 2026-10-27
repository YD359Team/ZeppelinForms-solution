namespace ZeppelinForms;

/// <summary>What a platform can do that another can't: asked before relying on it,
/// rather than learning it from an exception.</summary>
[Flags]
public enum PlatformCapabilities
{
    None = 0,

    /// <summary>A window places itself: <see cref="Forms.Form.Position"/> moves an open
    /// window, <see cref="Forms.Enums.WindowStartupLocation.Manual"/> opens it where the
    /// form says, <see cref="Forms.Form.CenterToScreen"/> centers it. Windows and X11 can;
    /// on Wayland the compositor alone decides where windows go, and those throw
    /// <see cref="PlatformNotSupportedException"/>; in the browser and on Android a form
    /// fills the page or the screen.</summary>
    WindowPositioning = 1 << 0,

    /// <summary>A window can leave the screen without closing: <see cref="Forms.Form.Hide"/>
    /// and <see cref="Forms.Form.Visible"/>. Windows, X11 and Wayland can; in the browser
    /// and on Android a form is the page or the screen, and Hide throws
    /// <see cref="PlatformNotSupportedException"/>.</summary>
    WindowHiding = 1 << 1,

    /// <summary>A <see cref="Forms.TrayIcon"/> shows: the notification area on Windows; on
    /// Linux a StatusNotifierItem host on the session bus — KDE, XFCE, Cinnamon, LXQt,
    /// waybar, GNOME with the AppIndicator extension — looked for the first time this is
    /// asked; an icon made before a panel came still appears when one does. The browser
    /// and Android have no tray.</summary>
    SystemTray = 1 << 2,

    /// <summary><see cref="Forms.TaskbarItem.ProgressState"/> shows: the taskbar button's
    /// progress on Windows; on Linux a session bus to send the launcher entry on — whether
    /// a dock shows it is the dock's: KDE's task manager, Dash to Dock and Plank do. Not in
    /// the browser and on Android.</summary>
    TaskbarProgress = 1 << 3,

    /// <summary><see cref="Forms.TaskbarItem.Badge"/> shows: an overlay in the corner of the
    /// taskbar button on Windows; the launcher entry's count on Linux, as for the progress;
    /// the badge of an installed web application in a browser that has one.</summary>
    TaskbarBadge = 1 << 4,

    /// <summary><see cref="Forms.Form.RequestAttention"/> works: the button flashes on
    /// Windows, the window is marked on X11 and, through xdg-activation, on Wayland; the
    /// tab's title blinks in the browser. Not on Android.</summary>
    WindowAttention = 1 << 5,
}