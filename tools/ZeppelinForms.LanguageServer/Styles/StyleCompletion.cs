using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using ZeppelinForms.LanguageServer.Markup;
using ZeppelinForms.LanguageServer.Protocol;

namespace ZeppelinForms.LanguageServer.Styles;

/// <summary>The types a selector names.</summary>
internal static partial class SelectorTypes
{
    /// <summary>The type of the element a selector selects: the last type name in it —
    /// <c>StackPanel.zebra &gt; Label:hover</c> → Label. Null — the selector names none:
    /// <c>.primary</c>, <c>&amp;:hover</c>.</summary>
    public static string? LastTypeName(string selector)
    {
        // the last compound of the last selector of a list
        string last = selector.Split(',')[^1].Trim();
        string[] compounds = last.Split((char[])[' ', '>', '+', '~'], StringSplitOptions.RemoveEmptyEntries);
        if (compounds.Length == 0) return null;

        Match type = TypeName().Match(compounds[^1]);
        return type.Success ? type.Value : null;
    }

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex TypeName();
}

/// <summary>
/// Completion in .zss: the controls, pseudo-classes and nesting of selectors; in a block,
/// the styled properties of the control its selector names; after a colon, their values —
/// enum members, the theme's <c>@Colors</c>, the sheet's <c>$variables</c>.
/// </summary>
internal static partial class StyleCompletion
{
    private static readonly string[] PseudoClasses =
    [
        "hover", "pressed", "focus", "focus-visible", "disabled", "checked", "selected", "open", "invalid",
        "first-child", "last-child", "odd", "even", "nth-child()", "not()",
    ];

    public static (IReadOnlyList<CompletionEntry> Items, int ReplaceFrom) Complete(string text, int position, MarkupSymbols symbols)
    {
        StyleContext context = StyleContext.At(text, position);
        var items = new List<CompletionEntry>();

        if (context.InComment) return (items, position);

        // the word being typed: letters, digits, '-', and what it starts with
        int start = position;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] is '-' or '_')) start--;

        char lead = start > 0 ? text[start - 1] : '\0';

        // "Label:hover" in a block is a nested rule, not a property named Label
        bool nestedSelector = context.InValue && context.Property is { } written &&
            PropertyType(context, symbols) is null && symbols.Controls().Any(t => t.Name == written);

        if (context.InValue && !nestedSelector)
        {
            if (lead == '@')
            {
                foreach (IPropertySymbol theme in symbols.ThemeNames())
                    items.Add(new CompletionEntry(theme.Name, CompletionKind.Color, SymbolText.Detail(theme), Documentation: SymbolText.Summary(theme)));

                return (items, start);
            }

            if (lead == '$')
            {
                foreach (string variable in Variables(text))
                    items.Add(new CompletionEntry(variable, CompletionKind.Variable));

                return (items, start);
            }

            if (PropertyType(context, symbols) is { } type)
                items.AddRange(MarkupCompletion.ValuesOf(type, symbols));

            return (items, start);
        }

        if (context.Depth > 0 && !nestedSelector && lead != ':' && lead != '.' && lead != '#' && lead != '&')
        {
            // a declaration's property — or a nested rule, which starts with a control or '&'
            INamedTypeSymbol? selected = SelectedType(context, symbols);

            foreach (var (name, valueType, descriptor) in symbols.StyledProperties(selected ?? symbols.Element!))
            {
                items.Add(new CompletionEntry(name, CompletionKind.Property, valueType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    Insert: name + ": $1;$0", Documentation: SymbolText.Summary(descriptor)));
            }

            items.Add(new CompletionEntry("transition", CompletionKind.Keyword, "transition: Property duration easing", Insert: "transition: $1;$0", Rank: 2));
        }

        if (lead == ':')
        {
            foreach (string pseudo in PseudoClasses)
                items.Add(new CompletionEntry(pseudo, CompletionKind.Keyword));

            return (items, start);
        }

        if (lead is '.' or '#') return (items, start);

        // a selector: the controls
        foreach (INamedTypeSymbol control in symbols.Controls())
            items.Add(new CompletionEntry(control.Name, CompletionKind.Class, control.ToDisplayString(), Documentation: SymbolText.Summary(control), Rank: context.Depth > 0 ? 3 : 0));

        return (items, start);
    }

    /// <summary>The control the innermost block styles: the last type its selector names,
    /// or its enclosing block's for <c>&amp;:hover</c>.</summary>
    public static INamedTypeSymbol? SelectedType(StyleContext context, MarkupSymbols symbols)
    {
        foreach (string selector in context.Selectors.Reverse())
        {
            if (SelectorTypes.LastTypeName(selector) is { } name &&
                symbols.Controls().FirstOrDefault(t => t.Name == name) is { } type)
            {
                return type;
            }
        }

        return null;
    }

    private static ITypeSymbol? PropertyType(StyleContext context, MarkupSymbols symbols)
    {
        if (context.Property is not { } property || symbols.Element is null) return null;

        // kebab case too: background-color is BackgroundColor
        string pascal = string.Concat(property.Split('-').Select(p => p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..]));

        return symbols.StyledProperties(SelectedType(context, symbols) ?? symbols.Element)
            .FirstOrDefault(p => p.Name.Equals(pascal, StringComparison.OrdinalIgnoreCase)).ValueType;
    }

    private static IEnumerable<string> Variables(string text) =>
        VariableDeclaration().Matches(text).Select(m => m.Groups[1].Value).Distinct();

    [GeneratedRegex(@"\$([A-Za-z_][A-Za-z0-9_-]*)\s*:")]
    private static partial Regex VariableDeclaration();
}

/// <summary>Where in a style sheet a position is: the selectors of the blocks around it, and
/// whether it is in a declaration's value.</summary>
internal sealed class StyleContext
{
    public required IReadOnlyList<string> Selectors { get; init; }

    public int Depth => Selectors.Count;

    public bool InComment { get; init; }

    /// <summary>After the colon of a declaration in a block.</summary>
    public bool InValue { get; init; }

    /// <summary>The declaration's property, when <see cref="InValue"/>.</summary>
    public string? Property { get; init; }

    public static StyleContext At(string text, int position)
    {
        var selectors = new List<string>();
        int statementStart = 0;
        int i = 0;

        while (i < position)
        {
            char c = text[i];

            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);

                if (end < 0 || end + 2 > position)
                    return new StyleContext { Selectors = selectors, InComment = true };

                i = end + 2;
                continue;
            }

            if (c is '"' or '\'')
            {
                int end = text.IndexOf(c, i + 1);
                i = end < 0 ? position : end + 1;
                continue;
            }

            switch (c)
            {
                case '{':
                    selectors.Add(text[statementStart..i].Trim());
                    statementStart = i + 1;
                    break;

                case '}':
                    if (selectors.Count > 0) selectors.RemoveAt(selectors.Count - 1);
                    statementStart = i + 1;
                    break;

                case ';':
                    statementStart = i + 1;
                    break;
            }

            i++;
        }

        string statement = text[statementStart..position];
        int colon = statement.IndexOf(':');

        // in a block, "name:" starts a value — unless it is a nested selector's pseudo-class,
        // "&:hover", whose colon follows no plain property name
        bool inValue = selectors.Count > 0 && colon > 0 &&
            statement[..colon].Trim() is { Length: > 0 } name &&
            name.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '$');

        return new StyleContext
        {
            Selectors = selectors,
            InValue = inValue || (selectors.Count == 0 && colon > 0 && statement.TrimStart().StartsWith('$')),
            Property = inValue ? statement[..colon].Trim() : null,
        };
    }
}
