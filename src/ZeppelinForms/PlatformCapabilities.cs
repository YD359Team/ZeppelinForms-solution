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
}