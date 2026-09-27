using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Forms.Controls.Tools;

public static class PropertyCatalog
{
    private static readonly Dictionary<Type, PropertyDescriptor[]> Registry = [];

    public static void Register(Type type, PropertyDescriptor[] properties) =>
        Registry[type] = properties;

    /// <summary>Explicit registration plus everything from the styled property registry.
    /// On a name match the explicit one wins: it defines the order and the editors.</summary>
    public static PropertyDescriptor[] For(Type type)
    {
        PropertyDescriptor[] declared = Registry.TryGetValue(type, out var props) ? props : [];

        var names = new HashSet<string>(declared.Select(p => p.Name), StringComparer.Ordinal);

        return
        [
            .. declared,
            .. StyledProperty.For(type)
                .Where(p => !names.Contains(p.Name))
                .Select(FromStyled),
        ];
    }

    private static PropertyDescriptor FromStyled(StyledProperty property) =>
        new(property.Name,
            property.ValueType,
            target => property.GetBoxed((UIElement)target),
            (target, value) => property.SetBoxedAsUser((UIElement)target, value))
        {
            Category = property.Category,
        };
}