using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms;

// ITextCompositionTarget moved to Forms/Interfaces and became public in 0.16:
// a text field of another package needs it as much as TextBox does

/// <summary>Input methods: the text they compose, the text they commit, and where the
/// focused field's caret is for their windows.</summary>
public partial class Form
{
    /// <summary>The caret rectangle last told to the input method.</summary>
    private Rectangle? _textInputArea;

    /// <summary>Whether the focused element draws composed text in place. Where it
    /// doesn't, a backend that has to put something on screen while composing — the
    /// on-screen keyboards of Android — types the composition as it goes.</summary>
    internal bool FocusedAcceptsComposition =>
        _focusDispatcher.FocusedElement is ITextCompositionTarget { AcceptsComposition: true };

    /// <summary>The input method's text being composed changed. Not part of the text yet:
    /// the field shows it underlined at the caret. Empty: the composition ended without
    /// a result — cancelled.</summary>
    internal void OnCompositionUpdate(string text, int cursor)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (_focusDispatcher.FocusedElement is ITextCompositionTarget { AcceptsComposition: true } target)
            target.SetComposition(text, Math.Clamp(cursor, 0, text.Length));
    }

    /// <summary>The input method committed text: it ends the composition and goes in as
    /// typed characters, through the form's <see cref="TextInput"/> as a key's would.</summary>
    internal void OnCompositionCommit(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (_focusDispatcher.FocusedElement is ITextCompositionTarget target)
            target.SetComposition(string.Empty, 0);

        foreach (char c in text)
            OnTextInput(c);
    }

    /// <summary>A text field's caret, in the window's coordinates, for the input method's
    /// windows. Only the focused element's counts. Elements report through
    /// <see cref="UIElement.ReportTextInputArea(Rectangle)"/>, in their own coordinates.</summary>
    internal void ReportTextInputArea(UIElement element, Rectangle caret)
    {
        if (ReferenceEquals(element, _focusDispatcher.FocusedElement))
            SetTextInputArea(caret);
    }

    private void SetTextInputArea(Rectangle? area)
    {
        if (_textInputArea == area) return;

        _textInputArea = area;

        if (PlatformWindow is ITextInputMethodWindow window)
            window.SetTextInputArea(area);
    }
}