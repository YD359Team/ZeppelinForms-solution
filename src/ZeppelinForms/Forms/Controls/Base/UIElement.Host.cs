using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>The element and the form it is in: finding the form, taking the focus,
/// telling the input method where the caret is. What a control of another package
/// needs from its form, without the framework's internals.</summary>
public abstract partial class UIElement
{
    /// <summary>The form the element is in — through its parents, or as an overlay of
    /// the form: a flyout, a popup. Null — the element isn't in a form yet, or any more.</summary>
    /// <remarks>
    /// Read it when it is needed rather than keeping it: an element moves between forms,
    /// and <see cref="OnAttached"/> and <see cref="OnDetached"/> tell when. Cheap — the
    /// answer is cached until the tree changes.
    /// </remarks>
    public Form? FindForm() => FindOwner();

    /// <summary>Move the keyboard focus to the element.</summary>
    /// <returns>Whether the element has the focus now. False when it doesn't take the
    /// focus — it isn't an <see cref="IInputElement"/>, or its
    /// <see cref="IInputElement.TabStop"/> is off — when it, or a container of it, is
    /// disabled, and when it isn't in a form.</returns>
    /// <remarks>Programmatic focus: the focus ring isn't shown for it unless the
    /// user is already working the form from the keyboard, see <see cref="IsFocusVisible"/>.</remarks>
    public bool Focus() => FindOwner() is { } form && form.FocusElement(this);

    /// <summary>Tell the input method where the element's caret is, in the element's
    /// own coordinates: the candidate list of an East Asian input method opens under it,
    /// and an on-screen keyboard keeps it in sight.</summary>
    /// <remarks>
    /// <para>
    /// Called by a text field as it draws its caret, so the input method follows every
    /// move of it. Only the focused element's report counts: an element that draws
    /// a caret while it isn't focused changes nothing.
    /// </para>
    /// <para>
    /// Until the first report the field's box stands for the caret: the form reports it
    /// itself when an element with <see cref="AcceptsTextInput"/> takes the focus. The
    /// text being composed comes through <see cref="ITextCompositionTarget"/>.
    /// </para>
    /// </remarks>
    protected void ReportTextInputArea(Rectangle caret)
    {
        if (FindOwner() is not { } form) return;

        Point absolute = GetAbsolutePosition();
        form.ReportTextInputArea(this, caret.Offset(absolute.X, absolute.Y));
    }
}