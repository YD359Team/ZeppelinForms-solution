using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>Showing focus: whether the element's focus is to be drawn,
/// and room for decoration drawn past the element's bounds.</summary>
public abstract partial class UIElement
{
    /// <summary>The element has focus, and the user is working the form from the
    /// keyboard — the focus ring should be seen. Like :focus-visible in CSS and
    /// FocusState.Keyboard in WinUI.</summary>
    /// <remarks>
    /// A ring after every click is noise: the user sees where they clicked without
    /// it, and every button they pressed kept a frame until the next click elsewhere.
    /// A keyboard user has no other way to know where the next key goes, so for them
    /// the ring is required. The form tells the two apart by the last input — see
    /// <see cref="Form.IsFocusVisible"/>. Text fields don't rely on this: the place
    /// where typing goes must be seen however the field was reached.
    /// </remarks>
    public bool IsFocusVisible =>
        this is IInputElement { IsFocused: true } && FindOwner() is { IsFocusVisible: true };

    /// <summary>How far the element's own drawing sticks out of its bounds — say,
    /// a focus ring around an indicator standing at the very edge.</summary>
    /// <remarks>
    /// Taken into the dirty bounds: without it the part of the drawing outside
    /// the bounds is not repainted, and a ring that went away leaves a sliver behind.
    /// The value must not depend on state such as focus — the dirty bounds are read
    /// when the state has already changed, and must still cover the old drawing.
    /// </remarks>
    protected virtual Thickness VisualOverflow => Thickness.Zero;
}