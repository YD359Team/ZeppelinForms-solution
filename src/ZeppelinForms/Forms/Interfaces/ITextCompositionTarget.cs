namespace ZeppelinForms.Forms.Interfaces;

/// <summary>An element that shows the text an input method is composing, in place.</summary>
/// <remarks>
/// <para>
/// Implemented by a text field: the form hands the composition to the focused element
/// when it implements this interface and <see cref="AcceptsComposition"/> is true.
/// An element without it still gets the committed text, character by character,
/// through its text input — the composition just isn't shown in place.
/// </para>
/// <para>
/// The element also tells the input method where its caret is, as it draws it, with
/// <c>UIElement.ReportTextInputArea</c>: the candidate list opens there. And it says
/// it takes text at all with <c>UIElement.AcceptsTextInput</c>, which raises the
/// on-screen keyboard on a touch screen.
/// </para>
/// </remarks>
public interface ITextCompositionTarget
{
    /// <summary>Whether it takes composition now: a password field doesn't — input
    /// methods are off for them — and its characters come committed.</summary>
    bool AcceptsComposition { get; }

    /// <summary>The text being composed, and where in it the input method's cursor
    /// stands. Empty: there is nothing being composed any more.</summary>
    void SetComposition(string text, int cursor);
}