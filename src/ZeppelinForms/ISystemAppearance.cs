using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Theming;

namespace ZeppelinForms;

/// <summary>
/// The system's appearance settings: light or dark, and the user's accent color.
/// </summary>
/// <remarks>
/// <para>
/// A platform implements this where the system has such settings, like
/// <see cref="ISystemMotionSettings"/>: on Windows it's "Choose your app mode" and
/// the accent color of Personalization, in the browser — the prefers-color-scheme
/// media query and the CSS AccentColor, on Android — the night mode and, from
/// Android 12, the dynamic colors of Material You. The platform hands it to
/// <see cref="App.UseSystemAppearance"/> at startup; an application follows it
/// with <see cref="App.UseSystemTheme(Theme, Theme, bool)"/>.
/// </para>
/// <para>
/// <see cref="Changed"/> is raised on the UI thread: the subscriber switches the
/// theme, and the theme restyles the open forms right in the handler. A platform
/// whose notification comes from elsewhere — a D-Bus signal, a callback of the
/// system — marshals it first.
/// </para>
/// </remarks>
public interface ISystemAppearance
{
    /// <summary>The user prefers dark applications.</summary>
    bool IsDark { get; }

    /// <summary>The user's accent color; null where the system has none
    /// or doesn't tell it.</summary>
    Color? AccentColor { get; }

    /// <summary>The accent with the shades the system derives from it, where it
    /// gives them: Windows keeps a whole palette, Android 12 a tonal one. By default —
    /// the accent alone, and a theme derives the shades it needs itself.</summary>
    AccentPalette? AccentPalette => AccentColor is { } accent ? new AccentPalette(accent) : null;

    /// <summary>The user turned on high contrast: the system's own contrast
    /// theme on Windows, forced colors in the browser, "higher contrast" of the
    /// desktop portal, high-contrast text on Android. By default — off.</summary>
    bool IsHighContrast => false;

    /// <summary>The colors of the system's contrast theme, where the system has
    /// them — Windows and the browser give the user's own; null — a built-in
    /// palette is used, light or dark by <see cref="IsDark"/>.</summary>
    HighContrastPalette? HighContrastPalette => null;

    /// <summary>The user changed the mode, the accent or the contrast while the
    /// application was running. Raised on the UI thread.</summary>
    event EventHandler? Changed;
}