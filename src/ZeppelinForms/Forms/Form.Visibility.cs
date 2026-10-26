namespace ZeppelinForms.Forms;

/// <summary>Hiding a form without closing it: a window kept for later — a settings
/// window opened again where it was left, a main window the tray icon brings back.</summary>
/// <remarks>
/// <para>
/// A hidden form keeps its window, its elements, its focus and its state; it leaves the
/// screen and the taskbar, and <see cref="Show()"/> brings it back as it was.
/// <see cref="Load"/> isn't raised again — it is of the first showing — while
/// <see cref="Shown"/> is, as for every show, and <see cref="VisibleChanged"/> is both ways.
/// </para>
/// <para>
/// A hidden form is still open: it is in <see cref="OpenForms"/>, and an application
/// whose main form is hidden goes on running — the way "close to the tray" is made:
/// cancel <see cref="Closing"/> and hide.
/// </para>
/// <para>
/// Windows, X11 and Wayland hide a window; in the browser and on Android a form is the
/// page or the screen and can't be hidden — check
/// <see cref="PlatformCapabilities.WindowHiding"/>.
/// </para>
/// </remarks>
public partial class Form
{
    private bool _visible;

    /// <summary>The window the form was shown in: a later <see cref="Show()"/> of the same
    /// window is a return from <see cref="Hide"/>, not a first showing.</summary>
    private IPlatformWindow? _shownWindow;

    /// <summary>The window is on the screen: from <see cref="Show()"/> until
    /// <see cref="Hide"/> or the window's closing. Setting it shows or hides the form,
    /// as in WinForms.</summary>
    public bool Visible
    {
        get => _visible;
        set
        {
            if (value) Show();
            else Hide();
        }
    }

    /// <summary><see cref="Visible"/> changed: the form was shown, hidden, shown again
    /// or closed.</summary>
    public event EventHandler? VisibleChanged;

    protected virtual void OnVisibleChanged(EventArgs e) => VisibleChanged?.Invoke(this, e);

    /// <summary>Take the window off the screen without closing it; <see cref="Show()"/>
    /// brings it back as it was. Nothing happens to a form that isn't shown.</summary>
    /// <exception cref="InvalidOperationException">The form is an open dialog: its owner
    /// waits for its result, and a dialog ends with <see cref="Accept"/> or
    /// <see cref="Cancel"/>.</exception>
    /// <exception cref="PlatformNotSupportedException">In the browser and on Android,
    /// where a form is the page or the screen.</exception>
    public void Hide()
    {
        if (!_visible) return;

        // the owner is disabled until the dialog ends: hidden, it would never end
        if (_dialogClosed is not null)
            throw new InvalidOperationException(
                "A dialog can't be hidden: its owner waits for it. Close it with Accept or Cancel.");

        IDesktopWindow window = DesktopWindow
            ?? throw new PlatformNotSupportedException(
                "This platform can't hide a window: in the browser and on Android a form is the page " +
                "or the screen. Check App.Capabilities for WindowHiding.");

        // the popups are part of the form's picture: they don't wait on the screen for it
        CloseAllFlyouts();

        window.SetVisible(false);
        SetVisibleState(false);
    }

    /// <summary>A window already shown in this life: on the screen it is left alone, a
    /// hidden one comes back — focus, layout and all. False — the window has to be shown
    /// for the first time.</summary>
    private bool TryShowAgain()
    {
        if (PlatformWindow is not { } window || _isClosed || !ReferenceEquals(_shownWindow, window))
            return false;

        if (_visible) return true;

        DesktopWindow?.SetVisible(true);
        SetVisibleState(true);

        return true;
    }

    /// <summary>The window was shown for the first time in its life.</summary>
    private void OnFirstShown()
    {
        if (PlatformWindow is not { } window) return;

        _shownWindow = window;
        SetVisibleState(true);
    }

    private void SetVisibleState(bool visible)
    {
        if (_visible == visible) return;

        _visible = visible;
        OnVisibleChanged(EventArgs.Empty);
    }
}