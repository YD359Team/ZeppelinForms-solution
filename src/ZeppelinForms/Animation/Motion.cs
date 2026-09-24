namespace ZeppelinForms.Animation;

public enum MotionPreference
{
    /// <summary>Whatever the system decided. Where there is no system
    /// setting, animation is on.</summary>
    System,

    /// <summary>Always animate, whatever the system says.</summary>
    Full,

    /// <summary>Never animate, whatever the system says.</summary>
    Reduced,
}

/// <summary>
/// Whether to reduce motion: one answer for the whole framework.
/// </summary>
/// <remarks>
/// Reduced motion does not mean "turn everything off": property transitions,
/// layout travel, enter and exit, page changes and the theme ripple become
/// instant, because this is motion for the sake of looks. Scroll inertia and
/// loading indicators stay: the first is a direct consequence of the user's
/// gesture, the second tells that work is going on, and without motion
/// that meaning would be lost.
/// </remarks>
public static class Motion
{
    private static ISystemMotionSettings? s_system;

    /// <summary>The application's decision on top of the system's.</summary>
    public static MotionPreference Preference
    {
        get;
        set
        {
            if (field == value) return;

            bool before = IsReduced;
            field = value;

            if (before != IsReduced) Changed?.Invoke(null, EventArgs.Empty);
        }
    } = MotionPreference.System;

    /// <summary>Whether motion should be reduced right now.</summary>
    public static bool IsReduced => Preference switch
    {
        MotionPreference.Reduced => true,
        MotionPreference.Full => false,
        _ => s_system?.PrefersReducedMotion ?? false,
    };

    /// <summary>The answer changed — because of the application or because of the system.</summary>
    public static event EventHandler? Changed;

    /// <summary>Connect the system setting. Called by the platform at startup.</summary>
    public static void UseSystemSettings(ISystemMotionSettings settings)
    {
        if (s_system is not null) s_system.Changed -= OnSystemChanged;

        s_system = settings;
        s_system.Changed += OnSystemChanged;
    }

    private static void OnSystemChanged(object? sender, EventArgs e)
    {
        // the application's decision overrides the system — in that case
        // a change in the system changes nothing, and there is nothing to report
        if (Preference == MotionPreference.System)
            Changed?.Invoke(null, EventArgs.Empty);
    }
}