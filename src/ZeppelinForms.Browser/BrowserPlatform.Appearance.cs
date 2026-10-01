using System.Globalization;
using System.Text.RegularExpressions;
using ZeppelinForms.Drawing.Primitives;

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
    private EventHandler? _appearanceChanged;

    public bool IsDark => _isDark;

    public Color? AccentColor => _accent;

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

        App.UseSystemAppearance(this);
    }

    /// <summary>Re-read both, report only a real change.</summary>
    internal void RefreshAppearance()
    {
        bool dark = Interop.PrefersDarkColorScheme();
        Color? accent = ParseCssColor(Interop.SystemAccentColor());

        if (dark == _isDark && accent == _accent) return;

        _isDark = dark;
        _accent = accent;

        _appearanceChanged?.Invoke(this, EventArgs.Empty);
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