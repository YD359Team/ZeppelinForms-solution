namespace ZeppelinForms.Forms.Styling;

/// <summary>
/// A marker for the generator: expand a partial property into a styled one —
/// register a <see cref="StyledProperty{T}"/>, create a field and write
/// accessors that take the value source into account.
/// </summary>
/// <remarks>
/// The property must be <c>partial</c> with a getter and a setter, and its type
/// must be a <c>partial</c> descendant of <c>UIElement</c>.
/// The default is set by a static property named <c>&lt;Name&gt;Default</c>;
/// if there is none, <c>default</c> of the value type is used.
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class StyledAttribute : Attribute
{
    /// <summary>The section in PropertyGrid.</summary>
    public string Category { get; set; } = "Other";

    /// <summary>A change of the value requires a layout pass,
    /// not just a redraw.</summary>
    public bool AffectsLayout { get; set; }

    /// <summary>The value is inherited down the tree, like the font.</summary>
    public bool Inherits { get; set; }

    /// <summary>
    /// The value lives not in a field of the element but in another object —
    /// like TextBox's text, which is stored in a TextDocument.
    /// </summary>
    /// <remarks>
    /// The generator creates only the StyledProperty registration; the field and
    /// accessors are written by the control itself, and the property stays
    /// ordinary (not partial). The setter must go through
    /// <c>SetValue(Property, value)</c> — otherwise the write bypasses
    /// the theme and bindings.
    /// </remarks>
    public bool External { get; set; }
}