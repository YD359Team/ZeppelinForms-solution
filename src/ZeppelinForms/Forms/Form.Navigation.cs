using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms;

/// <summary>Where a request to go back came from.</summary>
public enum BackSource : byte
{
    /// <summary>The application: <see cref="Form.RequestBack"/> called from code, a back
    /// button of its own.</summary>
    Code,

    /// <summary>The system's back button or gesture: Android's.</summary>
    System,

    /// <summary>The browser's back button: the page's history went one entry back.</summary>
    Browser,

    /// <summary>The keyboard: Alt+Left, or Escape for what closes with it.</summary>
    Keyboard,
}

/// <summary>Nothing in the form took a back request: the application may.</summary>
public sealed class BackNavigationEventArgs(BackSource source) : EventArgs
{
    public BackSource Source { get; } = source;

    /// <summary>The application handled it. Unhandled, a system back closes the activity
    /// on Android, and a dialog form is cancelled.</summary>
    public bool Handled { get; set; }
}

/// <summary>A part of the form that a back request may go to: a navigation stack with
/// somewhere to go back to, a bottom sheet to close.</summary>
internal interface IBackHandler
{
    /// <summary>Take the request if it is for this part now — it is visible and has
    /// somewhere to go back to. True — taken.</summary>
    bool TryHandleBack(BackSource source);

    /// <summary>Whether Escape goes to it too: a sheet closes with it, a navigation
    /// stack doesn't — Escape in a page is the page's.</summary>
    bool ClosesOnEscape { get; }
}

/// <summary>
/// Going back, the system's insets and the keyboard: what a form needs to behave as an
/// application on a phone and in a browser, the same way on a desktop.
/// </summary>
public partial class Form
{
    private readonly List<IBackHandler> _backHandlers = [];

    /// <summary>A back request nothing in the form took: no flyout to close, no
    /// <see cref="Controls.Navigation.NavigationHost"/> with a page to go back to, no
    /// sheet. Set <see cref="BackNavigationEventArgs.Handled"/> to keep it — "press back
    /// again to exit".</summary>
    public event EventHandler<BackNavigationEventArgs>? BackRequested;

    /// <summary>
    /// Go back, as the system's back button does: the topmost of what can — an open
    /// flyout closes, a bottom sheet closes, the innermost visible
    /// <see cref="Controls.Navigation.NavigationHost"/> goes to its previous page —
    /// and when none of them could, <see cref="BackRequested"/>.
    /// </summary>
    /// <returns>Whether something took the request. A dialog form nobody else answered
    /// for is cancelled, unless the request came from code.</returns>
    /// <remarks>
    /// The platforms call it for their own back: Android's button and gesture, the
    /// browser's back button, Alt+Left on a desktop. An application's own back button
    /// calls it too, and goes wherever the system's would.
    /// </remarks>
    public bool RequestBack(BackSource source = BackSource.Code)
    {
        // what lies on top first: a menu or a picker open over the page
        if (_flyouts.Count > 0)
        {
            CloseFlyout(_flyouts[^1]);
            return true;
        }

        // the latest first: a sheet over the page, a stack inside a stack — the
        // part registered last is the one on top or innermost
        for (int i = _backHandlers.Count - 1; i >= 0; i--)
        {
            if (i < _backHandlers.Count && _backHandlers[i].TryHandleBack(source))
                return true;
        }

        var args = new BackNavigationEventArgs(source);
        BackRequested?.Invoke(this, args);

        if (args.Handled) return true;

        // a dialog nobody kept is left the way Escape leaves it
        if (IsDialog && source != BackSource.Code)
        {
            Cancel();
            return true;
        }

        return false;
    }

    /// <summary>A part that takes back requests: registered when it is attached to the
    /// form, in tree order, so an inner stack comes after the outer one.</summary>
    internal void AddBackHandler(IBackHandler handler)
    {
        if (!_backHandlers.Contains(handler))
            _backHandlers.Add(handler);
    }

    internal void RemoveBackHandler(IBackHandler handler) => _backHandlers.Remove(handler);

    /// <summary>Escape for what closes with it: the topmost sheet.</summary>
    private bool TryCloseWithEscape()
    {
        for (int i = _backHandlers.Count - 1; i >= 0; i--)
        {
            IBackHandler handler = _backHandlers[i];

            if (handler.ClosesOnEscape && handler.TryHandleBack(BackSource.Keyboard))
                return true;
        }

        return false;
    }

    // ===== the safe area =====

    /// <summary>
    /// How far the system's own bars and cutouts reach into the client area, in
    /// device-independent pixels: the status bar, a camera's notch, the gesture bar.
    /// Zero where nothing covers the form — on a desktop, and on Android unless the
    /// form draws under the bars (<see cref="EdgeToEdge"/>).
    /// </summary>
    /// <remarks>
    /// <see cref="Controls.SafeArea"/> keeps its content out of it; an app bar and a
    /// navigation bar stretch their background under it and keep their content out.
    /// In a browser the page's <c>env(safe-area-inset-*)</c>: nonzero on a phone with a
    /// notch when the page's viewport says <c>viewport-fit=cover</c>.
    /// </remarks>
    public Thickness SafeAreaInsets { get; private set; }

    /// <summary><see cref="SafeAreaInsets"/> changed: the screen turned, the gesture bar
    /// moved.</summary>
    public event EventHandler? SafeAreaInsetsChanged;

    /// <summary>
    /// Lay the form out under the system's bars, as an application whose app bar is
    /// colored up to the top edge: the bars' area is reported in
    /// <see cref="SafeAreaInsets"/>, and the controls keep out of it themselves.
    /// False — the form is placed between the bars, and the insets are zero. Android's;
    /// elsewhere nothing covers the form, or — a browser — the page decides.
    /// </summary>
    public bool EdgeToEdge
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            (PlatformWindow as IEdgeToEdgeWindow)?.OnEdgeToEdgeChanged();
        }
    }

    /// <summary>The platform measured the insets anew.</summary>
    internal void SetSafeAreaInsets(Thickness insets)
    {
        if (SafeAreaInsets == insets) return;

        SafeAreaInsets = insets;

        Invalidate();
        SafeAreaInsetsChanged?.Invoke(this, EventArgs.Empty);
    }

    // ===== the keyboard =====

    /// <summary>
    /// The client area got shorter — on a phone, most often, because the keyboard came
    /// up under the field being typed in: the field is brought back into view, inside
    /// whatever scrolls around it, once the new layout is done.
    /// </summary>
    private void KeepFocusedFieldInView(Size previous, Size current)
    {
        if (current.Height >= previous.Height) return;
        if (_focusDispatcher.FocusedElement is not { AcceptsTextInput: true }) return;

        Schedule(0, () =>
        {
            if (_focusDispatcher.FocusedElement is not { AcceptsTextInput: true } focused) return;

            UpdateLayout();
            focused.BringIntoView(KeyboardMargin);
        });
    }

    /// <summary>Room left between a field brought into view and the keyboard's edge.</summary>
    private const float KeyboardMargin = 12f;
}

/// <summary>A platform window that lays its form out under the system's bars when the
/// form asks for it.</summary>
internal interface IEdgeToEdgeWindow
{
    void OnEdgeToEdgeChanged();
}
