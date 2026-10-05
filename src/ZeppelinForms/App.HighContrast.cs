using ZeppelinForms.Theming;

namespace ZeppelinForms;

/// <summary>The system's high contrast over any theme.</summary>
/// <remarks>
/// A person who turned high contrast on needs it, rather than prefers it: low
/// vision, a light sensitivity, a projector in a lit room. So it wins over every
/// theme, including one the application chose in code — as in WinUI, where a
/// requested theme has no say while contrast is on. An application with a reason
/// to keep its own look — a drawing tool showing true colors — turns
/// <see cref="RespectHighContrast"/> off.
/// </remarks>
public partial class App
{
    /// <summary>Let the system's high contrast replace the theme while it is on.
    /// On by default.</summary>
    public static bool RespectHighContrast
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            ApplyEffectiveTheme();
        }
    } = true;

    /// <summary>Whether the contrast theme is in effect right now.</summary>
    public static bool IsHighContrastActive =>
        RespectHighContrast && s_appearance?.IsHighContrast == true;

    /// <summary>The contrast theme built last, and its palette: the theme is
    /// rebuilt only when the colors change, not on every Changed.</summary>
    private static (HighContrastPalette Palette, Theme Theme)? s_highContrast;

    /// <summary>The contrast theme to show; null — contrast is off or not respected.</summary>
    private static Theme? HighContrastTheme()
    {
        if (!IsHighContrastActive || s_appearance is not { } appearance) return null;

        HighContrastPalette palette = appearance.HighContrastPalette
            ?? (appearance.IsDark ? HighContrastPalette.Black : HighContrastPalette.White);

        if (s_highContrast is { } cached && cached.Palette == palette)
            return cached.Theme;

        Theme theme = Themes.HighContrast(palette);
        s_highContrast = (palette, theme);

        return theme;
    }
}