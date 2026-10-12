namespace ZeppelinForms.Forms.Markup;

/// <summary>
/// The property the child elements of a control go to in <c>.zfml</c> markup.
/// </summary>
/// <remarks>
/// Without it the markup generator tries Children, Items, Nodes, Content and Child, in
/// this order, and takes the first that accepts the child: a collection of the child's
/// type, or a property of it. A control whose children belong elsewhere marks that
/// property.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class ContentAttribute : Attribute;
