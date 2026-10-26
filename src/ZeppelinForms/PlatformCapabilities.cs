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
}