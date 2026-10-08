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
            .. StylingRows(type),
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
            StyledProperty = property,
        };

    /// <summary>The rows of an element's styling, on top: its classes — editable,
    /// so a class can be tried on in a running application — its pseudo-classes now,
    /// and the styles that match it, the strongest first.</summary>
    private static PropertyDescriptor[] StylingRows(Type type) =>
        !typeof(UIElement).IsAssignableFrom(type) ? [] :
        [
            new("Classes", typeof(string),
                target => ((UIElement)target).Classes.ToString(),
                (target, value) => SetClasses((UIElement)target, value as string))
            {
                Category = "Styling",
            },

            new("PseudoClasses", typeof(string),
                target => string.Join(" ", ((UIElement)target).PseudoClasses))
            {
                Category = "Styling",
            },

            new("Styles", typeof(string),
                target => DescribeStyles(((UIElement)target).GetMatchedStyles()))
            {
                Category = "Styling",
            },
        ];

    /// <summary>Replace the classes with those typed: names that are not identifiers
    /// yet — the user is in the middle of typing — are left out rather than thrown at.</summary>
    private static void SetClasses(UIElement element, string? text)
    {
        var wanted = new List<string>();

        foreach (string name in (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (!char.IsDigit(name[0]) && name.All(c => char.IsLetterOrDigit(c) || c is '-' or '_'))
                wanted.Add(name);

        foreach (string existing in element.Classes.ToArray())
            if (!wanted.Contains(existing, StringComparer.Ordinal))
                element.Classes.Remove(existing);

        foreach (string name in wanted)
            element.Classes.Add(name);
    }

    private static string DescribeStyles(IReadOnlyList<Style> styles)
    {
        if (styles.Count == 0) return "—";

        return string.Join("; ", styles.Reverse().Select(style => style.Source is { } source
            ? $"{style.Selector} ({Path.GetFileName(source)})"
            : style.Selector.ToString()));
    }
}