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
/// change itself — <c>ConfigurationChanges = ConfigChanges.UiMode | ConfigChanges.FontScale</c> in its
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
    private bool _isHighContrast;
    private float _textScale = 1f;
    private AccentPalette? _accent;
    private EventHandler? _appearanceChanged;

    public bool IsDark => _isDark;

    /// <summary>"High contrast text" of the accessibility settings. Android gives
    /// no palette for it: the built-in one is used, light or dark by the night mode.</summary>
    public bool IsHighContrast => _isHighContrast;

    /// <summary>"Font size" of the display settings — the same multiplier the
    /// system applies to sp units.</summary>
    public float TextScale => _textScale;

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
        _isHighContrast = QueryHighContrast(_activity);
        _textScale = _activity.Resources?.Configuration?.FontScale ?? 1f;

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
        bool highContrast = QueryHighContrast(_activity);
        float textScale = configuration?.FontScale ?? 1f;

        if (dark == _isDark && accent == _accent && highContrast == _isHighContrast &&
            textScale == _textScale)
        {
            return;
        }

        _isDark = dark;
        _accent = accent;
        _isHighContrast = highContrast;
        _textScale = textScale;

        _appearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool QueryDark(Configuration? configuration) =>
        configuration is not null &&
        (configuration.UiMode & UiMode.NightMask) == UiMode.NightYes;

    /// <summary>The setting behind "High contrast text": a secure setting any
    /// application may read, the same the system's own apps look at. There is no
    /// notification for it; it is re-read on returning from the background,
    /// which is where the user changes it.</summary>
    private static bool QueryHighContrast(Activity activity) =>
        activity.ContentResolver is { } resolver &&
        global::Android.Provider.Settings.Secure.GetInt(resolver, "high_text_contrast_enabled", 0) == 1;

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