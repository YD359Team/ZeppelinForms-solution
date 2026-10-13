using Android.Views;

namespace ZeppelinForms.Android;

/// <summary>Android has no title bar: the system bars are where the form's choice goes —
/// light icons over a dark app, dark ones over a light app. The bars' color itself is
/// the app's to draw behind them: from API 35 every app is edge to edge, and the
/// system ignores a color set for them.</summary>
public sealed partial class AndroidPlatform
{
    // WindowInsetsController.APPEARANCE_LIGHT_STATUS_BARS and _NAVIGATION_BARS
    private const int AppearanceLightStatusBars = 1 << 3;
    private const int AppearanceLightNavigationBars = 1 << 4;

    /// <summary>Whether the window is the bottom one: the bars are its, not a dialog's.</summary>
    internal bool IsRoot(AndroidWindow window) => ReferenceEquals(Root, window);

    internal void SetSystemBarsDark(bool dark)
    {
        global::Android.Views.Window? window = _activity.Window;
        if (window is null) return;

        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            const int light = AppearanceLightStatusBars | AppearanceLightNavigationBars;

            window.InsetsController?.SetSystemBarsAppearance(dark ? 0 : light, light);
            return;
        }

        // before API 30 the same through the decor view's flags: the status bar's icons
        // from API 23, the navigation bar's from 26
#pragma warning disable CS0618, CA1422
        View decor = window.DecorView;
        var flags = (SystemUiFlags)(int)decor.SystemUiVisibility;

        SystemUiFlags lightBars = SystemUiFlags.LightStatusBar;

        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            lightBars |= SystemUiFlags.LightNavigationBar;

        flags = dark ? flags & ~lightBars : flags | lightBars;
        decor.SystemUiVisibility = (StatusBarVisibility)(int)flags;
#pragma warning restore CS0618, CA1422
    }
}