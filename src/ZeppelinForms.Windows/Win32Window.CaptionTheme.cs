using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Windows;

/// <summary>The title bar as the form asks for it: dark or light, and on Windows 11
/// the caption's own colors.</summary>
/// <remarks>
/// The form decides — from its TitleBarTheme and TitleBarColor and the application's
/// theme, not the system's mode: an application in its dark theme on a light system
/// still wants a dark caption — and calls this when it is shown, before the window is
/// seen, and whenever any of them changes.
/// The window used to follow App.ThemeChanged by itself, and its first call came from
/// the top of Create, before the window had a handle: it did nothing, and a dark theme
/// opened under a light caption until the theme was switched.
/// </remarks>
internal sealed partial class Win32Window
{
    public void SetTitleBarAppearance(bool isDark, Color? background, Color? foreground)
    {
        if (_handle == 0) return;

        Win32Appearance.SetDarkCaption(_handle, isDark);
        Win32Appearance.SetCaptionColors(_handle, background, foreground);
    }
}