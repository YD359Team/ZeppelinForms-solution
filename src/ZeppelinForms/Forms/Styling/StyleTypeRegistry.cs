using System.ComponentModel;

namespace ZeppelinForms.Forms.Styling;

/// <summary>
/// Where an assembly lists the controls a style sheet may name. Not for application
/// code: the ZeppelinForms generator calls it from a module initializer of every
/// assembly with controls of its own.
/// </summary>
/// <remarks>
/// Naming the controls with <c>typeof</c> in the assembly itself is what lets a
/// trimmed or natively compiled application keep them and find them by name; see
/// the generated <c>ZeppelinForms.Generated.StyleTypeRegistration</c>.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class StyleTypeRegistry
{
    /// <param name="types">The assembly's types derived from <c>UIElement</c> that its
    /// own code can name: public and internal ones, generic ones left out.</param>
    /// <param name="initialize">Runs their static initializers, which register their
    /// styled properties and pseudo-classes. Called once, when the style system first
    /// needs them.</param>
    public static void Register(Type[] types, Action initialize)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(initialize);

        StyleTypes.Register(types, initialize);
    }
}
