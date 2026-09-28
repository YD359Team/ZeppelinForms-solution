using System.Globalization;
using Android.Views;
using Android.Views.InputMethods;
using Java.Lang;
using ZeppelinForms.Forms;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.Android;

/// <summary>A bridge between the IME and the form.</summary>
/// <remarks>
/// fullEditor: false means we don't keep an editable buffer on the Android side —
/// the text lives in the form's TextDocument. Simple IME presses then come as key
/// events, and suggestions, autocorrection and emoji — through CommitText.
/// </remarks>
internal sealed class ZeppelinInputConnection(View view, Form form)
    : BaseInputConnection(view, fullEditor: false)
{
    /// <summary>How many visible characters of composing text stand in the field
    /// right now. The IME replaces that text on every change, so it is erased
    /// before the next version goes in.</summary>
    private int _composing;

    /// <summary>The final text: it replaces whatever is still being composed.</summary>
    public override bool CommitText(ICharSequence? text, int newCursorPosition)
    {
        ReplaceComposing(text?.ToString() ?? string.Empty);
        _composing = 0;

        return true;
    }

    /// <summary>The word being typed, in its current version.</summary>
    /// <remarks>
    /// Keyboards with suggestions send a growing word — "h", "he", "hel" — and each
    /// call replaces the previous one. This used to call CommitText, so every version
    /// was inserted on top of the last and "hel" came out as "hhehel". There is still
    /// no underlined "being composed" state in TextDocument, and faking it here is
    /// not our business; but the text itself must be right.
    /// </remarks>
    public override bool SetComposingText(ICharSequence? text, int newCursorPosition)
    {
        string value = text?.ToString() ?? string.Empty;

        ReplaceComposing(value);
        _composing = new StringInfo(value).LengthInTextElements;

        return true;
    }

    /// <summary>The composed text stays as it is — it simply stops being replaceable.</summary>
    public override bool FinishComposingText()
    {
        _composing = 0;

        return true;
    }

    /// <summary>Autocorrection and suggestions come as "erase so many characters and
    /// insert this": the erasing must be done, otherwise the word doubles.</summary>
    public override bool DeleteSurroundingText(int beforeLength, int afterLength)
    {
        Press(Key.Backspace, beforeLength);
        Press(Key.Delete, afterLength);

        return true;
    }

    private void ReplaceComposing(string text)
    {
        // Backspace in TextDocument erases a visible character, not a char —
        // exactly the unit _composing is counted in
        Press(Key.Backspace, _composing);

        foreach (char c in text)
            form.OnTextInput(c);
    }

    private void Press(Key key, int count)
    {
        for (int i = 0; i < count; i++)
        {
            form.OnKeyDown(key, KeyModifiers.None, false);
            form.OnKeyUp(key, KeyModifiers.None);
        }
    }
}