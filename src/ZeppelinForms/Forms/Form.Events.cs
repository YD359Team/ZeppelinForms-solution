using System.ComponentModel;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.Forms;

/// <summary>The form's life and input, as events and as protected virtual methods.</summary>
/// <remarks>
/// <para>
/// Every event has its On-method, the WinForms way: a form of the application
/// overrides the method, code from outside subscribes to the event. An override
/// that doesn't call the base method keeps the event from being raised.
/// </para>
/// <para>
/// The life of a form: <see cref="Load"/> once, before the window is first seen;
/// <see cref="Shown"/> on every show; <see cref="Activated"/> and
/// <see cref="Deactivated"/> as the user moves between windows;
/// <see cref="Closing"/>, which may keep the form open; <see cref="Closed"/>.
/// </para>
/// <para>
/// Input reaches the form around its elements. <see cref="PreviewKeyDown"/> and
/// <see cref="PreviewMouseWheel"/> come before any element — the place for the
/// window's own shortcuts; <see cref="KeyDown"/>, <see cref="KeyUp"/> and
/// <see cref="TextInput"/> get what the focused element left. The pointer events
/// only observe: they come before the elements and can't take the pointer from them.
/// </para>
/// </remarks>
public partial class Form
{
    // ===== Life =====

    /// <summary>Raised once, before the window is first seen: the place to fill
    /// the form with data. Showing the form again doesn't raise it again.</summary>
    public event EventHandler? Load;

    /// <summary>The form is about to close. Set <see cref="CancelEventArgs.Cancel"/>
    /// to keep it open. Raised for the close button of the title bar as well as for
    /// <see cref="Close"/>, <see cref="Accept"/> and <see cref="Cancel"/>.</summary>
    public event EventHandler<FormClosingEventArgs>? Closing;

    /// <summary>The window became the active one: the one the keyboard types into.</summary>
    public event EventHandler? Activated;

    /// <summary>Another window became the active one, or the application lost the focus.</summary>
    public event EventHandler? Deactivated;

    protected virtual void OnLoad(EventArgs e) => Load?.Invoke(this, e);

    protected virtual void OnShown(EventArgs e) => Shown?.Invoke(this, e);

    protected virtual void OnClosing(FormClosingEventArgs e) => Closing?.Invoke(this, e);

    protected virtual void OnClosed(EventArgs e) => Closed?.Invoke(this, e);

    protected virtual void OnActivated(EventArgs e) => Activated?.Invoke(this, e);

    protected virtual void OnDeactivated(EventArgs e) => Deactivated?.Invoke(this, e);

    protected virtual void OnWindowStateChanged(EventArgs e) => WindowStateChanged?.Invoke(this, e);

    protected virtual void OnClientSizeChanged(EventArgs e) => ClientSizeChanged?.Invoke(this, e);

    private bool _closing;

    /// <summary>Ask whether the form may close: raises <see cref="Closing"/>.
    /// Called by <see cref="Close"/> and by the platforms, for the close button.</summary>
    /// <returns>true — close; false — a handler kept the form open.</returns>
    internal bool RequestClose(CloseReason reason)
    {
        // nothing is open — there is nobody to ask
        if (_isClosed || PlatformWindow is null) return true;

        // Close from inside a Closing handler: the close is already being decided,
        // and the outer one goes on unless the handler cancels it
        if (_closing) return false;

        _closing = true;

        try
        {
            var args = new FormClosingEventArgs(reason);
            OnClosing(args);

            return !args.Cancel;
        }
        finally
        {
            _closing = false;
        }
    }

    // ===== Keyboard =====

    /// <summary>A key was pressed, before any element sees it — menus, access keys
    /// and the focused element included. Set Handled to keep the key for the window:
    /// a shortcut that works wherever the focus is.</summary>
    public event EventHandler<KeyEventArgs>? PreviewKeyDown;

    /// <summary>A key was pressed and no element took it — before the window's own
    /// keys: Tab, Escape, Enter. Set Handled to keep those from it.</summary>
    public event EventHandler<KeyEventArgs>? KeyDown;

    /// <summary>A key was released and no element took it.</summary>
    public event EventHandler<KeyEventArgs>? KeyUp;

    /// <summary>A character was typed, before the focused element gets it. Set
    /// Handled to keep it from the element.</summary>
    public event EventHandler<TextInputEventArgs>? TextInput;

    protected virtual void OnPreviewKeyDown(KeyEventArgs e) => PreviewKeyDown?.Invoke(this, e);

    protected virtual void OnKeyDown(KeyEventArgs e) => KeyDown?.Invoke(this, e);

    protected virtual void OnKeyUp(KeyEventArgs e) => KeyUp?.Invoke(this, e);

    protected virtual void OnTextInput(TextInputEventArgs e) => TextInput?.Invoke(this, e);

    // ===== Pointer =====

    /// <summary>A pointer — mouse, finger, pen — was pressed anywhere in the window.
    /// Observes only: the element under the pointer gets the press all the same.</summary>
    public event EventHandler<PointerEventArgs>? PointerPressed;

    /// <summary>A pointer moved anywhere in the window. Observes only.</summary>
    public event EventHandler<PointerEventArgs>? PointerMoved;

    /// <summary>A pointer was released anywhere in the window. Observes only.</summary>
    public event EventHandler<PointerEventArgs>? PointerReleased;

    /// <summary>The wheel turned, before the element under the pointer sees it.
    /// Set Handled to keep it for the window: zooming with Ctrl+wheel.</summary>
    public event EventHandler<MouseWheelEventArgs>? PreviewMouseWheel;

    protected virtual void OnPointerPressed(PointerEventArgs e) => PointerPressed?.Invoke(this, e);

    protected virtual void OnPointerMoved(PointerEventArgs e) => PointerMoved?.Invoke(this, e);

    protected virtual void OnPointerReleased(PointerEventArgs e) => PointerReleased?.Invoke(this, e);

    protected virtual void OnPreviewMouseWheel(MouseWheelEventArgs e) => PreviewMouseWheel?.Invoke(this, e);
}