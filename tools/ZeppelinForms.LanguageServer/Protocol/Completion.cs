using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace ZeppelinForms.LanguageServer.Protocol;

/// <summary>The protocol's kinds of completion items: the icon the editor shows.</summary>
internal enum CompletionKind
{
    Method = 2,
    Field = 5,
    Variable = 6,
    Class = 7,
    Module = 9,
    Property = 10,
    Value = 12,
    Keyword = 14,
    Snippet = 15,
    Color = 16,
    EnumMember = 20,
    Event = 23,
}

/// <summary>One item of a completion list.</summary>
/// <param name="Label">What the list shows and what is matched against the typed word.</param>
/// <param name="Insert">What goes in, when it isn't the label: <c>Text="$1"</c> — a snippet,
/// <c>$1</c> where the caret stays.</param>
/// <param name="Rank">The order before the editor's own: lower first.</param>
internal sealed record CompletionEntry(
    string Label, CompletionKind Kind, string? Detail = null, string? Insert = null, string? Documentation = null, int Rank = 1);

/// <summary>What completion and hover tell of a symbol: its declaration and its summary.</summary>
internal static class SymbolText
{
    public static string Detail(ISymbol symbol) => symbol switch
    {
        IPropertySymbol property => $"{property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {property.ContainingType.Name}.{property.Name}",
        IEventSymbol @event => $"event {@event.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {@event.ContainingType.Name}.{@event.Name}",
        IFieldSymbol field => $"{field.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {field.ContainingType.Name}.{field.Name}",
        IMethodSymbol method => method.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
        INamedTypeSymbol type => type.ToDisplayString(),
        _ => symbol.ToDisplayString(),
    };

    /// <summary>The summary of the symbol's documentation, as plain text with code in backticks.</summary>
    public static string? Summary(ISymbol symbol)
    {
        string? xml = symbol.GetDocumentationCommentXml(expandIncludes: false);
        if (string.IsNullOrWhiteSpace(xml)) return null;

        try
        {
            XElement? summary = XElement.Parse(xml).Element("summary");
            if (summary is null) return null;

            var text = new StringBuilder();

            foreach (XNode node in summary.Nodes())
            {
                switch (node)
                {
                    case XText plain:
                        text.Append(plain.Value);
                        break;

                    case XElement { Name.LocalName: "see" or "seealso" } see:
                        string? reference = (string?)see.Attribute("cref") ?? (string?)see.Attribute("langword");
                        text.Append('`').Append(reference is null ? see.Value : ShortName(reference)).Append('`');
                        break;

                    case XElement { Name.LocalName: "c" or "paramref" or "typeparamref" } code:
                        text.Append('`').Append(code.Value.Length > 0 ? code.Value : (string?)code.Attribute("name")).Append('`');
                        break;

                    case XElement other:
                        text.Append(other.Value);
                        break;
                }
            }

            return string.Join(" ", text.ToString().Split((char[])[' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries));
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>"T:ZeppelinForms.Forms.Form" → Form; "P:A.B.Text" → B.Text.</summary>
    private static string ShortName(string reference)
    {
        string name = reference.Length > 2 && reference[1] == ':' ? reference[2..] : reference;

        int paren = name.IndexOf('(');
        if (paren >= 0) name = name[..paren];

        string[] parts = name.Split('.');
        return reference.StartsWith("T:", StringComparison.Ordinal) || parts.Length < 2
            ? parts[^1]
            : parts[^2] + "." + parts[^1];
    }

    /// <summary>Markdown for hover: the declaration as code, the summary under it.</summary>
    public static string Markdown(ISymbol symbol)
    {
        var text = new StringBuilder();

        text.Append("```csharp\n").Append(Detail(symbol)).Append("\n```");

        if (Summary(symbol) is { } summary)
            text.Append("\n\n").Append(summary);

        return text.ToString();
    }
}
