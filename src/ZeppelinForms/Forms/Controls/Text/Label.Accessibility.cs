using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms.Controls.Text;

/// <summary>The label of another element.</summary>
public partial class Label
{
    /// <summary>The element this label names — the field after "Name:". Its
    /// accessible name becomes this label's text, unless it was given one of its
    /// own; the mnemonic of the label moves the focus there.</summary>
    /// <remarks>
    /// Sets the target's <see cref="UIElement.LabeledBy"/> only while that is free
    /// or already this label: an element named explicitly by another label keeps it.
    /// Retargeting releases the previous target the same way.
    /// </remarks>
    public UIElement? Target
    {
        get;
        set
        {
            if (ReferenceEquals(field, value)) return;

            if (field is not null && ReferenceEquals(field.LabeledBy, this))
                field.LabeledBy = null;

            field = value;

            if (value is not null && value.LabeledBy is null)
                value.LabeledBy = this;
        }
    }
}