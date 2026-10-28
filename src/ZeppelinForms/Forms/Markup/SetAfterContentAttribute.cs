namespace ZeppelinForms.Forms.Markup;

/// <summary>
/// A property that <c>.zfml</c> markup sets after the element's content, wherever its
/// attribute is written: a selected index means something only among the items.
/// </summary>
/// <remarks>
/// <c>&lt;ComboBox SelectedIndex="1"&gt;</c> with its items as children was set before the
/// items were there, and a combo box with no items took no selection. Marked, the property
/// waits for the children; a property of a control of one's own that depends on its content
/// is marked the same way.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class SetAfterContentAttribute : Attribute;
