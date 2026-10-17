using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms;

/// <summary>The window's title bar, as far as the platform lets an application decide it.</summary>
/// <remarks>
/// <para>
/// Windows draws its caption dark or light (10 from 1809), and in a color of the
/// application's (11). X11 passes the wish to a GNOME window manager. The browser takes
/// the page's color scheme and its theme color — the address bar of a phone, the title
/// bar of an installed app. Android has no title bar: its system bars' icons follow,
/// light over a dark app. Only the bottom form decides there, a dialog over it doesn't.
/// </para>
/// <para>
/// The form decides and tells the platform when it is shown — before the window is
/// seen, so a dark caption doesn't flash white first — and whenever the theme or these
/// properties change. With the system's high contrast on, no color of the form's own is
/// passed: the system's colors are the ones that must be read.
/// </para>
/// </remarks>
public partial class Form
{
    /// <summary>Light or dark. <see cref="TitleBarTheme.FollowApp"/>: as the theme is,
    /// or as <see cref="TitleBarColor"/> is when it is set.</summary>
    public TitleBarTheme TitleBarTheme
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            ApplyTitleBar();
        }
    }

    /// <summary>The title bar's own color: Windows 11 and the browser. Null — the
    /// system's. The caption's text takes black or white, whichever reads on it.
    /// <c>TitleBarColor = App.Theme.Colors.Background</c> makes the caption one
    /// with the window.</summary>
    public Color? TitleBarColor
    {
        get;
        set
        {
            if (Nullable.Equals(field, value)) return;

            field = value;
            ApplyTitleBar();
        }
    }

    /// <summary>Tell the platform what the title bar is to be.</summary>
    internal void ApplyTitleBar()
    {
        if (PlatformWindow is not { } window) return;

        // the system's high contrast colors are not ours to override
        Color? background = App.IsHighContrastActive ? null : TitleBarColor;

        bool dark = TitleBarTheme switch
        {
            TitleBarTheme.Light => false,
            TitleBarTheme.Dark => true,
            _ => background is { } color ? IsDarkColor(color) : App.Theme.Colors.IsDark,
        };

        // the caption's text and the system buttons' glyphs must read on the fill
        Color? foreground = background is { } fill
            ? IsDarkColor(fill) ? Colors.White : new Color(255, 0x1A, 0x1A, 0x1A)
            : null;

        window.SetTitleBarAppearance(dark, background, foreground);
    }

    /// <summary>Dark by perceived brightness, as a caption's text is chosen on it.</summary>
    private static bool IsDarkColor(Color color) =>
        (0.299f * color.R) + (0.587f * color.G) + (0.114f * color.B) < 128f;
}