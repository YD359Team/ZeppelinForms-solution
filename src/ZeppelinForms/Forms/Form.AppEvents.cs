using ZeppelinForms.Core.Globalization;
using ZeppelinForms.Theming;

namespace ZeppelinForms.Forms;

/// <summary>The application-wide events the form follows: the theme, the text scale and
/// the language.</summary>
/// <remarks>
/// All three are static: while the form is subscribed, they hold it together with its
/// whole tree. A closed form nobody disposes — every MessageBox, every InputBox — would
/// never be collected, so the form follows them from its constructor until its window
/// closes, and again from the next window on. A change made in between is caught up
/// with when the window comes.
/// <para>Until 0.20 each of the three was dropped and renewed in its own place, with its
/// own bookkeeping, and some of them twice; now one pair of methods keeps them together,
/// and calling either of them again changes nothing.</para>
/// </remarks>
public partial class Form
{
    /// <summary>The form is subscribed to the application's events.</summary>
    private bool _followsApp;

    /// <summary>The application as it was when the form stopped following it; null — the
    /// form follows it, or never stopped.</summary>
    private AppSnapshot? _appAtDetach;

    /// <summary>Subscribe to the theme, the text scale and the language, and apply what
    /// changed while the form wasn't subscribed. Nothing happens when already subscribed.</summary>
    private void FollowApp()
    {
        if (_followsApp) return;

        _followsApp = true;

        App.ThemeChanged += OnThemeChanged;
        App.TextScaleChanged += OnTextScaleChanged;
        Localization.Changed += OnLocalizationChanged;

        if (_appAtDetach is not { } before) return;

        _appAtDetach = null;

        // in the order the events would have come: the theme restyles the tree, the text
        // scale and the language measure it again
        if (!ReferenceEquals(before.Theme, App.Theme))
            OnThemeChanged(null, EventArgs.Empty);

        if (before.TextScale != App.TextScale)
            OnTextScaleChanged(null, EventArgs.Empty);

        if (before.LocalizationVersion != Localization.Version)
            OnLocalizationChanged(null, EventArgs.Empty);
    }

    /// <summary>Unsubscribe from the application's events and remember where it stood, for
    /// <see cref="FollowApp"/> to catch up with. Nothing happens when not subscribed.</summary>
    private void StopFollowingApp()
    {
        if (!_followsApp) return;

        _followsApp = false;

        App.ThemeChanged -= OnThemeChanged;
        App.TextScaleChanged -= OnTextScaleChanged;
        Localization.Changed -= OnLocalizationChanged;

        _appAtDetach = new AppSnapshot(App.Theme, App.TextScale, Localization.Version);
    }

    /// <summary>What the form compares with the application when it follows it again.</summary>
    private readonly record struct AppSnapshot(Theme Theme, float TextScale, int LocalizationVersion);
}
