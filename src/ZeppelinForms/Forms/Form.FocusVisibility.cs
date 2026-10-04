using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.Forms;

/// <summary>Whether the form's focus is to be drawn: by the last kind of input.</summary>
public partial class Form
{
    /// <summary>The user is working the form from the keyboard, so the focused
    /// element should show its focus ring. Turned on by a key press and off by
    /// a pointer press; programmatic focus doesn't change it.</summary>
    /// <remarks>
    /// <para>
    /// The same heuristic browsers use for :focus-visible. Nothing is shown until
    /// the user touches the keyboard: focus the form gives on showing is shown by
    /// text fields alone, which show focus anyway.
    /// </para>
    /// <para>
    /// A key pressed after a click turns the ring on for the clicked control too:
    /// someone who clicked a button and then pressed Space is on the keyboard now,
    /// and needs to see where the next key goes. Modifiers alone don't count —
    /// Ctrl held for a Ctrl+click is still pointer work.
    /// </para>
    /// </remarks>
    public bool IsFocusVisible { get; private set; }

    /// <summary>A key went down: the keyboard is in use.</summary>
    private void NoteKeyboardInput(Key key)
    {
        if (IsModifierKey(key)) return;

        SetFocusVisible(true);
    }

    /// <summary>A pointer went down: the mouse, a pen or a finger is in use.</summary>
    private void NotePointerInput()
    {
        SetFocusVisible(false);

        // the mouse is in charge now: menu mode, entered from the keyboard, ends
        ExitMenuModeOnPointer();
    }

    private void SetFocusVisible(bool visible)
    {
        if (IsFocusVisible == visible) return;

        IsFocusVisible = visible;

        // only the focused element draws anything by this flag; the rest of the
        // form looks the same either way and doesn't need repainting
        _focusDispatcher.FocusedElement?.InvalidateVisual();
    }

    private static bool IsModifierKey(Key key) => key is
        Key.Shift or Key.LeftShift or Key.RightShift or
        Key.Control or Key.LeftControl or Key.RightControl or
        Key.Alt or Key.LeftAlt or Key.RightAlt or
        Key.LeftWindows or Key.RightWindows;
}