using Microsoft.CodeAnalysis;
using ZeppelinForms.LanguageServer.Protocol;

namespace ZeppelinForms.LanguageServer.Markup;

/// <summary>Hover in .zfml: an element's type, an attribute's property or event, a step of a
/// binding's path — their declaration and summary.</summary>
internal static class MarkupHover
{
    public static (string Markdown, int Start, int End)? At(string text, int position, MarkupSymbols symbols)
    {
        MarkupContext context = MarkupScanner.At(text, position);

        int end = position;
        while (end < text.Length && MarkupScanner.IsNameChar(text[end])) end++;

        switch (context.Place)
        {
            case MarkupPlace.ElementName:
                {
                    string name = text[context.WordStart..end];
                    int dot = name.IndexOf('.');

                    if (symbols.TypeOf(name, context) is not { } type) return null;

                    // <Grid.Styles>: the property after the dot, when the pointer is on it
                    if (dot > 0 && position > context.WordStart + dot &&
                        MarkupSymbols.AttributeMembers(type).FirstOrDefault(m => m.Name == name[(dot + 1)..]) is { } property)
                    {
                        return (SymbolText.Markdown(property), context.WordStart, end);
                    }

                    return (SymbolText.Markdown(type), context.WordStart, end);
                }

            case MarkupPlace.AttributeName when context.Attribute is { } attribute && context.Tag is { } tag:
                {
                    if (symbols.TypeOf(tag.Name, context) is not { } type) return null;

                    string full = text[context.WordStart..end];
                    string member = full.Contains('.') ? full[(full.LastIndexOf('.') + 1)..] : full;

                    return MarkupSymbols.AttributeMembers(type).FirstOrDefault(m => m.Name == member) is { } symbol
                        ? (SymbolText.Markdown(symbol), context.WordStart, end)
                        : null;
                }

            case MarkupPlace.AttributeValue when context.Tag is { } tag && context.Attribute is { } attribute &&
                                                 tag.Attributes.TryGetValue(attribute, out string? value) &&
                                                 value.StartsWith("{Bind ", StringComparison.Ordinal):
                return BindingStep(text, position, context, symbols, value);
        }

        return null;
    }

    /// <summary>The step of a binding's path under the pointer.</summary>
    private static (string, int, int)? BindingStep(string text, int position, MarkupContext context, MarkupSymbols symbols, string value)
    {
        int pathStart = context.ValueStart + 6;

        string rest = value[6..];
        int comma = rest.IndexOf(',');
        string path = (comma < 0 ? rest : rest[..comma]).TrimEnd('}', ' ');

        // leading spaces after "Bind"
        int skipped = path.Length - path.TrimStart().Length;
        path = path.Trim();
        pathStart += skipped;

        if (position < pathStart || position > pathStart + path.Length) return null;

        string[] segments = path.Split('.');
        int offset = pathStart;
        int index = 0;

        for (; index < segments.Length; index++)
        {
            if (position <= offset + segments[index].Length) break;
            offset += segments[index].Length + 1;
        }

        if (index >= segments.Length) return null;

        INamedTypeSymbol? within = symbols.ClassOf(context);
        Dictionary<string, string> named = MarkupScanner.NamedElements(text);

        ITypeSymbol? source;
        int first;

        if (segments.Length > 1 && named.TryGetValue(segments[0], out string? element))
        {
            if (index == 0)
                return symbols.TypeOf(element, context) is { } elementType
                    ? (SymbolText.Markdown(elementType), offset, offset + segments[0].Length)
                    : null;

            source = symbols.TypeOf(element, context);
            first = 1;
        }
        else
        {
            source = symbols.DataTypeOf(context);
            first = 0;
        }

        if (source is null) return null;

        ITypeSymbol? owner = symbols.Walk(source, segments[first..index], within);
        IPropertySymbol? property = owner is null ? null : symbols.PathProperties(owner, within).FirstOrDefault(p => p.Name == segments[index]);

        return property is null ? null : (SymbolText.Markdown(property), offset, offset + segments[index].Length);
    }
}
