using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.Forms;

/// <summary>Keys and characters from the platform to the focused element, with the
/// window's own handling around it (<see cref="HandleKeyboardBeforeFocus"/>).</summary>
public partial class Form
{
    /// <summary>The keyboard follows focus: a field got it — show the keyboard,
    /// focus moved to a button or nowhere — hide it.</summary>
    private void OnFocusChangedForKeyboard(object? sender, UIElement? focused)
    {
        // the input method's windows follow the caret of a field; without one they
        // have nowhere to be. A field reports its caret as it draws; until then the
        // field's own box stands for it — now, while the press that focused it is
        // being handled: a browser opens a phone's keyboard only then
        if (focused is { AcceptsTextInput: true } field)
        {
            Point at = field.GetAbsolutePosition();
            SetTextInputArea(new Rectangle(at, new Size(1, field.ActualSize.Height)));
        }
        else
        {
            SetTextInputArea(null);
        }

        if (PlatformWindow is not ISoftKeyboard keyboard) return;

        if (focused is { AcceptsTextInput: true } input)
            keyboard.ShowSoftKeyboard(input.SoftKeyboardKind);
        else
            keyboard.HideSoftKeyboard();
    }

    internal void OnKeyDown(Key key, KeyModifiers modifiers, bool isRepeat)
    {
        Keyboard.OnDown(key, modifiers);

        // before dispatching: a handler that moves focus on this very key must
        // already find the form in keyboard mode, and the new focus shown
        NoteKeyboardInput(key);

        // a key closes a tooltip, as a pointer press does: the user is doing something
        // now, and the tip would cover what the key changes. Modifiers alone don't count
        // — Ctrl held over a button is not an answer to its tooltip. Tab closes the old
        // focus's tip here and the new focus opens its own (OnFocusChangedForToolTip)
        if (!IsModifierKey(key)) HideToolTip();

        if (key == Key.F12 || (key == Key.I && modifiers.HasFlag(KeyModifiers.Control) && modifiers.HasFlag(KeyModifiers.Shift)))
        {
            ToggleInspector();
            return;
        }

        // isRepeat went nowhere until 0.18: every platform reported it, and every
        // handler saw false
        var args = new KeyEventArgs(key, modifiers, isRepeat);

        // the form's preview goes before everything: a shortcut of the window
        // as a whole takes the key from menus and elements alike
        OnPreviewKeyDown(args);
        if (args.Handled) return;

        // menus and access keys go before the focused element
        if (HandleKeyboardBeforeFocus(key, modifiers)) return;

        // the preview goes from the root to the focused element
        UIElement? focused = _focusDispatcher.FocusedElement;

        if (focused is not null)
        {
            List<UIElement> chain = [];

            for (UIElement? current = focused; current is not null; current = current.Parent)
                chain.Add(current);

            chain.Reverse();

            foreach (UIElement element in chain)
            {
                element.RaisePreviewKeyDown(args);
                if (args.Handled) return;
            }
        }

        for (UIElement? current = focused; current is not null; current = current.Parent)
        {
            current.RaiseKeyDown(args);
            if (args.Handled) break;
        }

        // the form hears what no element took, before its own keys: Tab, Escape, Enter
        if (!args.Handled)
            OnKeyDown(args);

        // shortcuts after the focused element: a text field keeps Ctrl+C and Ctrl+Z,
        // and what it doesn't take — Ctrl+S — runs the form's command
        if (!args.Handled && TryKeyGesture(key, modifiers))
            args.Handled = true;

        if (!args.Handled && key == Key.Tab && Content is not null)
        {
            if (modifiers.HasFlag(KeyModifiers.Shift))
                _focusDispatcher.MovePrevious(Content);
            else
                _focusDispatcher.MoveNext(Content);
        }
        else if (!args.Handled)
        {
            // Escape and Enter for the window as a whole
            HandleKeyboardAfterFocus(key, modifiers);
        }
    }

    internal void OnKeyUp(Key key, KeyModifiers modifiers)
    {
        Keyboard.OnUp(key, modifiers);
        HandleKeyboardKeyUp(key);

        var args = new KeyEventArgs(key, modifiers);

        for (UIElement? current = _focusDispatcher.FocusedElement; current is not null; current = current.Parent)
        {
            current.RaiseKeyUp(args);
            if (args.Handled) break;
        }

        if (!args.Handled)
            OnKeyUp(args);
    }

    internal void OnTextInput(char c)
    {
        var args = new TextInputEventArgs(c);

        // the form sees the character first and may keep it from the field
        OnTextInput(args);
        if (args.Handled) return;

        _focusDispatcher.FocusedElement?.RaiseTextInput(c);
    }
}
