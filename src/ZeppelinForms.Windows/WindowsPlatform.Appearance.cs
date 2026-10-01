using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Theming;

namespace ZeppelinForms.Windows;

/// <summary>The system's app mode and accent, see <see cref="Win32Appearance"/>.</summary>
public partial class WindowsPlatform : ISystemAppearance
{
    private bool _isDark;
    private AccentPalette? _accent;
    private EventHandler? _appearanceChanged;

    public bool IsDark => _isDark;

    public Color? AccentColor => _accent?.Accent;

    /// <summary>The whole Windows palette: the shades WinUI draws with,
    /// rather than ones derived from the accent.</summary>
    public AccentPalette? AccentPalette => _accent;

    // explicit: ISystemMotionSettings has its own Changed, and one public event
    // would fire for both — a theme rebuilt on every animation setting and back
    event EventHandler? ISystemAppearance.Changed
    {
        add => _appearanceChanged += value;
        remove => _appearanceChanged -= value;
    }

    private void InitAppearance()
    {
        _isDark = Win32Appearance.AppsUseDarkTheme();
        _accent = Win32Appearance.ReadAccentPalette();

        App.UseSystemAppearance(this);
    }

    /// <summary>Re-read on WM_SETTINGCHANGE. The mode and the accent come with
    /// "ImmersiveColorSet", but the parameter is not checked: the read is cheap,
    /// and only a real change is reported.</summary>
    private void RefreshAppearance()
    {
        bool dark = Win32Appearance.AppsUseDarkTheme();
        AccentPalette? accent = Win32Appearance.ReadAccentPalette();

        if (dark == _isDark && accent == _accent) return;

        _isDark = dark;
        _accent = accent;

        _appearanceChanged?.Invoke(this, EventArgs.Empty);
    }
}