using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms;

/// <summary>
/// A window whose input method — the system's IME: Chinese, Japanese and Korean input,
/// handwriting, dictation — can be told where text is being typed, so its candidate
/// list and its own composition window open at the caret rather than in a corner.
/// </summary>
/// <remarks>
/// <para>
/// Checked by a cast, as <see cref="ISoftKeyboard"/> is: a backend without an input
/// method to tell has nothing to implement. The text being composed reaches the form
/// through its composition entry points and is drawn by the field itself, underlined.
/// </para>
/// </remarks>
public interface ITextInputMethodWindow
{
    /// <summary>The caret of the focused text field moved, in the window's logical
    /// coordinates; null — no text field has the focus.</summary>
    void SetTextInputArea(Rectangle? caret);
}
