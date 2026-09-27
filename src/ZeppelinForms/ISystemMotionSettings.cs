namespace ZeppelinForms;

/// <summary>
/// The system "reduce motion" setting: the user asked the interface to animate
/// less — because of vestibular disorders, migraines or simply because it is
/// more comfortable for them.
/// </summary>
/// <remarks>
/// A platform implements this where possible, like IClipboard or IAppLifecycle:
/// on Windows it's "Show animations in Windows", on Android — an animation duration
/// scale of zero, in the browser — the prefers-reduced-motion media query. Where
/// there is no setting, the application decides itself through Motion.Preference.
/// </remarks>
public interface ISystemMotionSettings
{
    bool PrefersReducedMotion { get; }

    /// <summary>The user changed the setting while the application was running.</summary>
    event EventHandler? Changed;
}