using Android.App;
using Android.Content.Res;
using ZeppelinForms.Theming;

// ZeppelinForms.Drawing.Primitives.Color and Android.Graphics.Color meet here
using Color = ZeppelinForms.Drawing.Primitives.Color;

namespace ZeppelinForms.Android;

/// <summary>The night mode, and from Android 12 the accent of Material You.</summary>
/// <remarks>
/// <para>
/// The mode changes on the fly only for an activity that declares it handles the
/// change itself — <c>ConfigurationChanges = ConfigChanges.UiMode</c> in its
/// [Activity] attribute. Then ZeppelinView receives the new configuration and passes
/// it here. Without the declaration Android recreates the activity, and the new
/// application starts in the new mode anyway.
/// </para>
/// <para>
/// The accent is the system's tonal palette, generated from the wallpaper or picked
/// by the user: accent1 tone 50 is the accent, tone 40 the shade for a light page and
/// tone 80 for a dark one — the tones Material 3 itself takes as primary in its light
/// and dark schemes. It is re-read with the configuration and on returning from the
/// background, where the user changes it.
/// </para>
/// </remarks>
public sealed partial class AndroidPlatform : ISystemAppearance
{
    private bool _isDark;
    private AccentPalette? _accent;
    private EventHandler? _appearanceChanged;

    public bool IsDark => _isDark;

    public Color? AccentColor => _accent?.Accent;

    public AccentPalette? AccentPalette => _accent;

    // explicit: ISystemMotionSettings has a Changed of its own
    event EventHandler? ISystemAppearance.Changed
    {
        add => _appearanceChanged += value;
        remove => _appearanceChanged -= value;
    }

    private void InitAppearance()
    {
        _isDark = QueryDark(_activity.Resources?.Configuration);
        _accent = QueryAccent(_activity);

        App.UseSystemAppearance(this);
    }

    /// <summary>The view received a new configuration.</summary>
    internal void HandleConfigurationChanged(Configuration? configuration) =>
        RefreshAppearance(configuration ?? _activity.Resources?.Configuration);

    /// <summary>Re-read on returning from the background.</summary>
    internal void RefreshAppearance() =>
        RefreshAppearance(_activity.Resources?.Configuration);

    private void RefreshAppearance(Configuration? configuration)
    {
        bool dark = QueryDark(configuration);
        AccentPalette? accent = QueryAccent(_activity);

        if (dark == _isDark && accent == _accent) return;

        _isDark = dark;
        _accent = accent;

        _appearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool QueryDark(Configuration? configuration) =>
        configuration is not null &&
        (configuration.UiMode & UiMode.NightMask) == UiMode.NightYes;

    /// <summary>The tones by resource name: the system_accent1_* colors appeared in
    /// API 31, and looking them up by name keeps the code free of identifiers the
    /// bindings of an older SDK don't have.</summary>
    private static AccentPalette? QueryAccent(Activity activity)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(31)) return null;

        Resources? resources = activity.Resources;

        if (resources is null) return null;

        Color? Tone(string name)
        {
            int id = resources.GetIdentifier(name, "color", "android");

            if (id == 0) return null;

            global::Android.Graphics.Color color = resources.GetColor(id, activity.Theme);

            return new Color(color.R, color.G, color.B);
        }

        if (Tone("system_accent1_500") is not { } accent) return null;

        return new AccentPalette(accent)
        {
            Dark1 = Tone("system_accent1_600"),
            Light2 = Tone("system_accent1_200"),
        };
    }
}