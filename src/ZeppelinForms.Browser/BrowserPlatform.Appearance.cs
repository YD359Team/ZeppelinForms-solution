using System.Globalization;
using System.Text.RegularExpressions;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Theming;

namespace ZeppelinForms.Browser;

/// <summary>The color scheme from prefers-color-scheme, the accent from the CSS
/// system color AccentColor.</summary>
/// <remarks>
/// The scheme comes with a change event of its own. The accent has none: it is
/// re-read when the scheme changes and when the tab becomes visible again — the
/// user changes it in the system settings, which means leaving the page. A browser
/// that doesn't know AccentColor gives no accent, and the theme keeps its own:
/// Chromium doesn't support it, Firefox and Safari do.
/// </remarks>
public sealed partial class BrowserPlatform : ISystemAppearance
{
    private bool _isDark;
    private Color? _accent;
    private bool _isHighContrast;
    private HighContrastPalette? _highContrast;
    private EventHandler? _appearanceChanged;

    public bool IsDark => _isDark;

    public Color? AccentColor => _accent;

    /// <summary>Forced colors: Windows contrast themes reach the page through it,
    /// and the browser hands over the user's colors as CSS system colors.</summary>
    public bool IsHighContrast => _isHighContrast;

    public HighContrastPalette? HighContrastPalette => _highContrast;

    // explicit: ISystemMotionSettings has a Changed of its own
    event EventHandler? ISystemAppearance.Changed
    {
        add => _appearanceChanged += value;
        remove => _appearanceChanged -= value;
    }

    private void InitAppearance()
    {
        _isDark = Interop.PrefersDarkColorScheme();
        _accent = ParseCssColor(Interop.SystemAccentColor());
        _isHighContrast = Interop.ForcedColorsActive();
        _highContrast = _isHighContrast ? ReadHighContrast() : null;

        App.UseSystemAppearance(this);
    }

    /// <summary>Re-read both, report only a real change.</summary>
    internal void RefreshAppearance()
    {
        bool dark = Interop.PrefersDarkColorScheme();
        Color? accent = ParseCssColor(Interop.SystemAccentColor());
        bool isHighContrast = Interop.ForcedColorsActive();
        HighContrastPalette? highContrast = isHighContrast ? ReadHighContrast() : null;

        if (dark == _isDark && accent == _accent &&
            isHighContrast == _isHighContrast && highContrast == _highContrast)
        {
            return;
        }

        _isDark = dark;
        _accent = accent;
        _isHighContrast = isHighContrast;
        _highContrast = highContrast;

        _appearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The forced colors; null when the browser didn't give all eight —
    /// a half palette would mix pairs, and the built-in one is used instead.</summary>
    private static HighContrastPalette? ReadHighContrast()
    {
        Color?[] colors = [.. Interop.SystemColors().Split('|').Select(ParseCssColor)];

        if (colors.Length != 8 || colors.Any(color => color is null)) return null;

        return new HighContrastPalette(
            colors[0]!.Value, colors[1]!.Value, colors[2]!.Value, colors[3]!.Value,
            colors[4]!.Value, colors[5]!.Value, colors[6]!.Value, colors[7]!.Value);
    }

    /// <summary>A computed color as getComputedStyle gives it: "rgb(r, g, b)"
    /// or "rgba(r, g, b, a)". Anything else — no accent.</summary>
    private static Color? ParseCssColor(string css)
    {
        Match match = CssRgb().Match(css);

        if (!match.Success) return null;

        static byte Channel(Group group) =>
            (byte)Math.Clamp(int.Parse(group.Value, CultureInfo.InvariantCulture), 0, 255);

        return new Color(Channel(match.Groups[1]), Channel(match.Groups[2]), Channel(match.Groups[3]));
    }

    /// <summary>Generated: the browser build is trimmed, and a regex built
    /// at run time would carry the whole interpreter along.</summary>
    [GeneratedRegex(@"rgba?\(\s*(\d+)[\s,]+(\d+)[\s,]+(\d+)")]
    private static partial Regex CssRgb();
}