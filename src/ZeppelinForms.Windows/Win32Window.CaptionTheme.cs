namespace ZeppelinForms.Windows;

/// <summary>The title bar follows the application's theme — not the system's mode:
/// an application in its dark theme on a light system still wants a dark caption.</summary>
internal sealed partial class Win32Window
{
    /// <summary>Paint the caption before the window is first shown, and follow
    /// the theme from then on.</summary>
    private void AttachCaptionTheme()
    {
        ApplyCaptionTheme();
        App.ThemeChanged += OnAppThemeChanged;
    }

    private void DetachCaptionTheme() => App.ThemeChanged -= OnAppThemeChanged;

    private void OnAppThemeChanged(object? sender, EventArgs e) => ApplyCaptionTheme();

    private void ApplyCaptionTheme()
    {
        if (_handle == 0) return;

        Win32Appearance.SetDarkCaption(_handle, App.Theme.Colors.IsDark);
    }
}