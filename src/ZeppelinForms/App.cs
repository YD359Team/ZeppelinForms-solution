using System.Reflection;
using ZeppelinForms.Core;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Forms;
using ZeppelinForms.Theming;

namespace ZeppelinForms;

public partial class App
{
    public static event EventHandler? ThemeChanged;

    public required Form MainForm { get; init; }
    public Icon? Icon { get; init; }

    /// <summary>The theme the application starts with, set right where the
    /// application is described: <c>new App(platform) { StartupTheme = Themes.FluentLight,
    /// MainForm = new MainForm() }</c>. Null — keep the current one.</summary>
    /// <remarks>
    /// <para>
    /// The theme itself is static — one for all forms — and C# doesn't let an
    /// object initializer set a static property, so <c>Theme = …</c> there doesn't
    /// compile, and an instance can't have a property of the same name. Without this
    /// the only way was a separate <c>App.Theme = …</c> line before the App, which
    /// nothing in the App's own description hinted at.
    /// </para>
    /// <para>
    /// Applied at once, not in <see cref="Run"/>: put first in the initializer, it is
    /// already in place when the main form is built, and the form is styled once
    /// rather than first classic and then again. It is an explicit choice, so like
    /// setting <see cref="Theme"/> it stops following the system.
    /// </para>
    /// </remarks>
    public Theme? StartupTheme
    {
        get;
        init
        {
            field = value;

            if (value is not null)
                Theme = value;
        }
    }

    private readonly IPlatform _platform;

    /// <summary>The theme in effect — what the forms are styled with.</summary>
    private static Theme _theme = Themes.Light;

    /// <summary>The theme the application asked for, by code or by following the
    /// system. The one in effect differs only while high contrast takes over.</summary>
    private static Theme _requestedTheme = Themes.Light;

    private static readonly Lock _themeLocker = new();

    /// <summary>The current theme. A change applies to all open forms.</summary>
    /// <remarks>
    /// <para>
    /// Setting it stops following the system, see
    /// <see cref="UseSystemTheme(Theme, Theme, bool)"/>: a theme chosen in code
    /// is the application's decision, and the next change in the system settings
    /// must not quietly take it back.
    /// </para>
    /// <para>
    /// While the system's high contrast is on, the getter returns the contrast
    /// theme — that is what the forms show; what was set is kept in
    /// <see cref="RequestedTheme"/> and comes back when contrast is turned off.
    /// </para>
    /// </remarks>
    public static Theme Theme
    {
        get
        {
            lock (_themeLocker) return _theme;
        }
        set
        {
            s_systemThemes = null;
            SetTheme(value);
        }
    }

    /// <summary>The theme the application asked for, by code or by following the
    /// system — the one in effect unless high contrast took over.</summary>
    public static Theme RequestedTheme => _requestedTheme;

    /// <summary>Switch the theme without touching the following of the system:
    /// the path both for code and for the system's changes.</summary>
    private static void SetTheme(Theme value)
    {
        _requestedTheme = value;
        ApplyEffectiveTheme();
    }

    /// <summary>Put in place what the forms are to show: the system's high contrast
    /// over everything, otherwise the requested theme.</summary>
    private static void ApplyEffectiveTheme()
    {
        Theme value = HighContrastTheme() ?? _requestedTheme;

        if (ReferenceEquals(_theme, value)) return;

        _theme = value;

        // the path to the font file is a property of the platform, not of the
        // styling: in the browser there are no system fonts, and a theme must
        // not take away an already loaded file. A theme is of course entitled
        // to set a path of its own
        Font.Default = value.BaseFont.FilePath is null && Font.Default.FilePath is { } path
            ? value.BaseFont.WithFile(path)
            : value.BaseFont;

        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    // ===== following the system =====

    private static ISystemAppearance? s_appearance;

    /// <summary>The pair to follow; null — not following.</summary>
    private static (Theme Light, Theme Dark)? s_systemThemes;

    private static bool s_followAccent;

    /// <summary>The last theme built from the system, and what it was built from:
    /// a Changed with nothing new in it — WM_SETTINGCHANGE comes for any setting —
    /// must not rebuild the theme and restyle every form.</summary>
    private static (Theme Base, AccentPalette? Accent, Theme Result)? s_lastSystemTheme;

    /// <summary>The system's appearance settings; null on a platform that has none.</summary>
    public static ISystemAppearance? SystemAppearance => s_appearance;

    /// <summary>Connect the system's appearance settings. Called by the platform
    /// at startup; null disconnects them.</summary>
    public static void UseSystemAppearance(ISystemAppearance? appearance)
    {
        if (s_appearance is not null) s_appearance.Changed -= OnSystemAppearanceChanged;

        s_appearance = appearance;

        if (s_appearance is not null) s_appearance.Changed += OnSystemAppearanceChanged;

        // UseSystemTheme may have come first — before the platform was created
        if (s_systemThemes is not null) ApplySystemTheme();

        // the system may be in high contrast already at startup
        ApplyEffectiveTheme();

        // and its text larger
        ApplyTextScale();
    }

    /// <summary>Follow the system: the light or the dark theme of the pair by the
    /// system's mode, recolored with the user's accent.</summary>
    /// <param name="followAccent">Take the system accent. Off — the themes keep
    /// their own accent, for an application with a brand color.</param>
    /// <remarks>
    /// <para>
    /// Works until <see cref="Theme"/> is set from code. It can be called before the
    /// platform is created: until the platform connects its settings the light theme
    /// is used, and the right one replaces it as soon as they arrive. On a platform
    /// without such settings the light theme simply stays.
    /// </para>
    /// <para>
    /// The accent goes in by each theme's <see cref="Theming.Theme.AccentRule"/>: a Fluent
    /// theme takes the system's shade for its page, a classic one the accent itself.
    /// </para>
    /// </remarks>
    public static void UseSystemTheme(Theme light, Theme dark, bool followAccent = true)
    {
        s_systemThemes = (light, dark);
        s_followAccent = followAccent;
        s_lastSystemTheme = null;

        ApplySystemTheme();
    }

    /// <summary>Follow the system with the classic Light and Dark themes.</summary>
    public static void UseSystemTheme() => UseSystemTheme(Themes.Light, Themes.Dark);

    /// <summary>Whether the theme follows the system right now.</summary>
    public static bool IsFollowingSystemTheme => s_systemThemes is not null;

    private static void OnSystemAppearanceChanged(object? sender, EventArgs e)
    {
        lock (_themeLocker)
        {
            if (s_systemThemes is not null) ApplySystemTheme();

            // contrast turned on or off, or its colors changed: whatever the
            // application follows or not, high contrast is the system's word
            ApplyEffectiveTheme();

            ApplyTextScale();
        }
    }

    private static void ApplySystemTheme()
    {
        if (s_systemThemes is not { } pair) return;

        Theme theme = s_appearance?.IsDark == true ? pair.Dark : pair.Light;
        AccentPalette? accent = s_followAccent ? s_appearance?.AccentPalette : null;

        if (accent is not null)
        {
            // the same base and the same accent — the same theme as last time
            if (s_lastSystemTheme is { } last &&
                ReferenceEquals(last.Base, theme) &&
                last.Accent == accent)
            {
                theme = last.Result;
            }
            else
            {
                Theme recolored = theme.WithAccent(accent);
                s_lastSystemTheme = (theme, accent, recolored);
                theme = recolored;
            }
        }

        SetTheme(theme);
    }


    public App(IPlatform platform)
    {
        _platform = platform;
    }

    public void Run()
    {
        this.MainForm.Icon ??= Assets.Logo;

        IPlatformWindow window = _platform.CreateWindow(this.MainForm);

        // await continuations must come back to the UI thread:
        // ShowDialogAsync and all async code in handlers rely on this
        SynchronizationContext.SetSynchronizationContext(
        new ZfSynchronizationContext(window));

        if (_platform is IAppLifecycle lifecycle)
            AttachLifecycle(lifecycle);

        this.MainForm.Show();

        _platform.Start();
    }

    private static void AttachLifecycle(IAppLifecycle lifecycle)
    {
        lifecycle.Paused += (_, _) =>
        {
            foreach (Form form in Form.OpenForms)
                form.SuspendFrames();
        };

        lifecycle.Resumed += (_, _) =>
        {
            foreach (Form form in Form.OpenForms)
                form.ResumeFrames();
        };
    }
}