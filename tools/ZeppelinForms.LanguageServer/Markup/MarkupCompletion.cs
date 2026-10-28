using Microsoft.CodeAnalysis;
using CodeAccessibility = Microsoft.CodeAnalysis.Accessibility;
using ZeppelinForms.LanguageServer.Protocol;

namespace ZeppelinForms.LanguageServer.Markup;

/// <summary>
/// Completion in .zfml: elements after <c>&lt;</c>, the properties and events of an element in
/// its tag, values in an attribute — enum members, handlers of the class, commands — and the
/// paths of <c>{Bind …}</c>, from the data context <c>x:DataType</c> names or a named element.
/// </summary>
internal static class MarkupCompletion
{
    public static (IReadOnlyList<CompletionEntry> Items, int ReplaceFrom) Complete(string text, int position, MarkupSymbols symbols)
    {
        MarkupContext context = MarkupScanner.At(text, position);

        return context.Place switch
        {
            MarkupPlace.ElementName => (Elements(context, symbols), context.WordStart),
            MarkupPlace.AttributeName => (Attributes(context, symbols), context.WordStart),
            MarkupPlace.AttributeValue => Value(text, position, context, symbols),
            _ => ([], position),
        };
    }

    // ===== elements =====

    private static List<CompletionEntry> Elements(MarkupContext context, MarkupSymbols symbols)
    {
        var items = new List<CompletionEntry>();
        string word = context.Word;

        // <Grid. — the properties a property element can name
        int dot = word.IndexOf('.');

        if (dot > 0)
        {
            if (symbols.TypeOf(word[..dot], context) is { } owner)
            {
                foreach (ISymbol member in MarkupSymbols.AttributeMembers(owner).OfType<IPropertySymbol>())
                    items.Add(new CompletionEntry(word[..dot] + "." + member.Name, CompletionKind.Property, SymbolText.Detail(member), Documentation: SymbolText.Summary(member)));
            }

            return items;
        }

        // <vm: — the types of the namespace the prefix stands for
        int colon = word.IndexOf(':');

        if (colon > 0)
        {
            string prefix = word[..colon];

            if (prefix == "x")
            {
                items.Add(new CompletionEntry("x:String", CompletionKind.Class, "A text item of a list"));
                return items;
            }

            if (MarkupSymbols.NamespaceOfPrefix(prefix, context) is { } ns)
            {
                foreach (INamedTypeSymbol type in symbols.TypesIn(ns).Where(MarkupSymbols.IsCreatable))
                    items.Add(TypeEntry(prefix + ":" + type.Name, type, symbols));
            }

            return items;
        }

        foreach (INamedTypeSymbol type in symbols.FrameworkTypes.Where(MarkupSymbols.IsCreatable))
            items.Add(TypeEntry(type.Name, type, symbols));

        // the prefixes declared: the way to the application's own types
        foreach (MarkupElement element in context.InnermostFirst)
        {
            foreach (string key in element.Attributes.Keys.Where(k => k.StartsWith("xmlns:", StringComparison.Ordinal)))
            {
                string prefix = key[6..];

                if (prefix != "x" && !items.Any(i => i.Label == prefix + ":"))
                    items.Add(new CompletionEntry(prefix + ":", CompletionKind.Module, element.Attributes[key], Rank: 0));
            }
        }

        items.Add(new CompletionEntry("x:String", CompletionKind.Class, "A text item of a list", Rank: 2));

        // the parent's property elements: <Form.Styles>, <Border.Child>
        if (context.Ancestors.LastOrDefault() is { IsPropertyElement: false } parent && symbols.TypeOf(parent.Name, context) is { } parentType)
        {
            foreach (IPropertySymbol property in MarkupSymbols.AttributeMembers(parentType).OfType<IPropertySymbol>())
            {
                if (property.Type.SpecialType != SpecialType.None || property.Type.TypeKind == TypeKind.Enum || property.Type.IsValueType)
                    continue;

                items.Add(new CompletionEntry(parent.Name + "." + property.Name, CompletionKind.Property,
                    SymbolText.Detail(property), Documentation: SymbolText.Summary(property), Rank: 2));
            }
        }

        return items;
    }

    private static CompletionEntry TypeEntry(string label, INamedTypeSymbol type, MarkupSymbols symbols) =>
        new(label, CompletionKind.Class, type.ToDisplayString(), Documentation: SymbolText.Summary(type),
            Rank: symbols.IsElement(type) ? 0 : 1);

    // ===== attributes =====

    private static List<CompletionEntry> Attributes(MarkupContext context, MarkupSymbols symbols)
    {
        var items = new List<CompletionEntry>();
        MarkupElement tag = context.Tag!;

        if (tag.IsPropertyElement) return items;

        bool root = context.Ancestors.Count == 0;
        var present = new HashSet<string>(tag.Attributes.Keys, StringComparer.Ordinal);

        if (context.Attribute is { } typing) present.Remove(typing);

        if (symbols.TypeOf(tag.Name, context) is { } type)
        {
            foreach (ISymbol member in MarkupSymbols.AttributeMembers(type))
            {
                if (present.Contains(member.Name)) continue;

                items.Add(new CompletionEntry(
                    member.Name,
                    member is IEventSymbol ? CompletionKind.Event : CompletionKind.Property,
                    SymbolText.Detail(member),
                    Insert: member.Name + "=\"$1\"$0",
                    Documentation: SymbolText.Summary(member)));
            }
        }

        void Add(string name, string detail, int rank = 2)
        {
            if (!present.Contains(name))
                items.Add(new CompletionEntry(name, CompletionKind.Keyword, detail, Insert: name + "=\"$1\"$0", Rank: rank));
        }

        Add("x:Name", "Names the element and makes a field of it");
        Add("x:DataType", "The type of the data context here and inside: {Bind Path} reads from it");
        Add("x:FieldModifier", "The field's access: private, internal, public");

        if (root)
        {
            Add("Class", "The class the markup builds: Namespace.Name", rank: 0);
            Add("xmlns:vm", "A prefix for a namespace of the application: using:App.ViewModels");
        }

        return items;
    }

    // ===== values =====

    private static (IReadOnlyList<CompletionEntry>, int) Value(string text, int position, MarkupContext context, MarkupSymbols symbols)
    {
        string attribute = context.Attribute!;
        string before = context.ValueBefore;
        MarkupElement tag = context.Tag!;

        if (attribute.StartsWith("xmlns:", StringComparison.Ordinal))
            return (Namespaces(symbols), context.ValueStart);

        if (attribute == "x:DataType")
            return (DataTypes(context, symbols), context.ValueStart);

        if (before.StartsWith('{'))
            return Extension(text, position, context, symbols);

        INamedTypeSymbol? type = symbols.TypeOf(tag.Name, context);
        if (type is null) return ([], context.ValueStart);

        // <Setter Property="…" Value="…"> inside <Style Selector="…">
        if (tag.Name == "Setter")
            return (SetterValue(context, symbols), context.ValueStart);

        string memberName = attribute.Contains('.') ? attribute[(attribute.LastIndexOf('.') + 1)..] : attribute;
        ISymbol? member = MarkupSymbols.AttributeMembers(type).FirstOrDefault(m => m.Name == memberName);

        var items = new List<CompletionEntry>();

        switch (member)
        {
            case IEventSymbol:
                // the class's methods: the handler a click goes to
                if (symbols.ClassOf(context) is { } @class)
                {
                    foreach (IMethodSymbol method in MarkupSymbols.Members(@class).OfType<IMethodSymbol>()
                        .Where(m => m.MethodKind == MethodKind.Ordinary && !m.IsStatic && m.Parameters.Length == 2 && m.ContainingType.Equals(@class, SymbolEqualityComparer.Default)))
                    {
                        items.Add(new CompletionEntry(method.Name, CompletionKind.Method, SymbolText.Detail(method)));
                    }
                }

                break;

            case IPropertySymbol property:
                items.AddRange(ValuesOf(property.Type, symbols));

                if (symbols.IsCommand(property.Type) && symbols.ClassOf(context) is { } owner)
                {
                    foreach (ISymbol command in MarkupSymbols.Members(owner).Where(m =>
                        m is IPropertySymbol { IsIndexer: false } p && symbols.IsCommand(p.Type) || m is IFieldSymbol f && symbols.IsCommand(f.Type)))
                    {
                        items.Add(new CompletionEntry(command.Name, CompletionKind.Field, SymbolText.Detail(command)));
                    }
                }

                // any property binds: a styled one both ways, another one way
                items.Add(new CompletionEntry("{Bind }", CompletionKind.Snippet, "A binding", Insert: "{Bind $1}", Rank: 3));
                break;
        }

        return (items, context.ValueStart);
    }

    /// <summary>The values a type has names for: enum members, true and false.</summary>
    public static IEnumerable<CompletionEntry> ValuesOf(ITypeSymbol type, MarkupSymbols symbols)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            type = nullable.TypeArguments[0];

        if (type.TypeKind == TypeKind.Enum)
        {
            foreach (IFieldSymbol field in type.GetMembers().OfType<IFieldSymbol>().Where(f => f.HasConstantValue))
                yield return new CompletionEntry(field.Name, CompletionKind.EnumMember, type.Name, Documentation: SymbolText.Summary(field));
        }
        else if (type.SpecialType == SpecialType.System_Boolean)
        {
            yield return new CompletionEntry("true", CompletionKind.Value);
            yield return new CompletionEntry("false", CompletionKind.Value);
        }
        else if (type is INamedTypeSymbol named && type.SpecialType == SpecialType.None)
        {
            // static members of its own type: Colors.Red, BoxShadow.Medium, Thickness.Zero
            foreach (ISymbol member in named.GetMembers().Where(m => m.IsStatic && m.DeclaredAccessibility == CodeAccessibility.Public &&
                (m is IPropertySymbol p && SymbolEqualityComparer.Default.Equals(p.Type, named) ||
                 m is IFieldSymbol f && SymbolEqualityComparer.Default.Equals(f.Type, named))))
            {
                yield return new CompletionEntry(member.Name, CompletionKind.Value, SymbolText.Detail(member), Documentation: SymbolText.Summary(member));
            }
        }
    }

    private static List<CompletionEntry> Namespaces(MarkupSymbols symbols)
    {
        var items = new List<CompletionEntry>();

        foreach (INamespaceSymbol ns in AllNamespaces(symbols.Compilation.Assembly.GlobalNamespace))
            if (ns.GetTypeMembers().Length > 0)
                items.Add(new CompletionEntry("using:" + ns.ToDisplayString(), CompletionKind.Module));

        return items;
    }

    private static IEnumerable<INamespaceSymbol> AllNamespaces(INamespaceSymbol root)
    {
        foreach (INamespaceSymbol inner in root.GetNamespaceMembers())
        {
            yield return inner;

            foreach (INamespaceSymbol deeper in AllNamespaces(inner))
                yield return deeper;
        }
    }

    /// <summary>x:DataType: the types of the namespaces the markup declares, as prefix:Type.</summary>
    private static List<CompletionEntry> DataTypes(MarkupContext context, MarkupSymbols symbols)
    {
        var items = new List<CompletionEntry>();

        foreach (MarkupElement element in context.InnermostFirst)
        {
            foreach ((string key, string value) in element.Attributes)
            {
                if (!key.StartsWith("xmlns:", StringComparison.Ordinal) || MarkupSymbols.ClrNamespace(value) is not { } ns) continue;

                foreach (INamedTypeSymbol type in symbols.TypesIn(ns).Where(t => !t.IsStatic))
                    items.Add(new CompletionEntry(key[6..] + ":" + type.Name, CompletionKind.Class, type.ToDisplayString(), Documentation: SymbolText.Summary(type)));
            }
        }

        if (symbols.ClassOf(context)?.ContainingNamespace is { IsGlobalNamespace: false } own)
        {
            foreach (INamedTypeSymbol type in own.GetTypeMembers())
                items.Add(new CompletionEntry(type.Name, CompletionKind.Class, type.ToDisplayString(), Rank: 2));
        }

        return items;
    }

    /// <summary>Property="…" of a setter: the styled properties of the type its style selects;
    /// Value="…": the values of that property.</summary>
    private static List<CompletionEntry> SetterValue(MarkupContext context, MarkupSymbols symbols)
    {
        var items = new List<CompletionEntry>();
        INamedTypeSymbol selected = SelectedType(context, symbols) ?? symbols.Element!;

        if (symbols.Element is null) return items;

        var properties = symbols.StyledProperties(selected).ToList();

        if (context.Attribute == "Property")
        {
            foreach (var (name, valueType, descriptor) in properties)
                items.Add(new CompletionEntry(name, CompletionKind.Property, valueType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), Documentation: SymbolText.Summary(descriptor)));
        }
        else if (context.Attribute == "Value" && context.Tag!.Attributes.TryGetValue("Property", out string? property) &&
                 properties.FirstOrDefault(p => p.Name == property) is { ValueType: not null } chosen)
        {
            items.AddRange(ValuesOf(chosen.ValueType, symbols));

            foreach (IPropertySymbol theme in symbols.ThemeNames())
                items.Add(new CompletionEntry("@" + theme.Name, CompletionKind.Color, SymbolText.Detail(theme), Documentation: SymbolText.Summary(theme), Rank: 2));
        }

        return items;
    }

    /// <summary>The control a style is for: the last type its selector names, looking out
    /// through the enclosing styles for "^:hover".</summary>
    private static INamedTypeSymbol? SelectedType(MarkupContext context, MarkupSymbols symbols)
    {
        foreach (MarkupElement element in context.Ancestors.Reverse())
        {
            if (element.Name != "Style" || !element.Attributes.TryGetValue("Selector", out string? selector)) continue;

            if (Styles.SelectorTypes.LastTypeName(selector) is { } name &&
                symbols.Controls().FirstOrDefault(t => t.Name == name) is { } type)
            {
                return type;
            }
        }

        return null;
    }

    // ===== {Bind …} =====

    private static (IReadOnlyList<CompletionEntry>, int) Extension(string text, int position, MarkupContext context, MarkupSymbols symbols)
    {
        string before = context.ValueBefore;

        if (!before.StartsWith("{Bind ", StringComparison.Ordinal))
        {
            return ([
                new CompletionEntry("{Bind }", CompletionKind.Snippet, "A binding: a path from the data context or a named element", Insert: "{Bind $1}"),
                new CompletionEntry("{x:Null}", CompletionKind.Keyword, "Null"),
            ], context.ValueStart);
        }

        string body = before[6..];

        // after a comma: the options
        int comma = body.LastIndexOf(',');

        if (comma >= 0)
        {
            string option = body[(comma + 1)..].TrimStart();
            int from = position - option.Length;

            return ([
                new CompletionEntry("Mode=TwoWay", CompletionKind.Keyword, "The element's changes go to the source"),
                new CompletionEntry("Mode=OneWay", CompletionKind.Keyword, "The source's changes go to the element"),
            ], from);
        }

        string path = body.TrimStart();
        string[] segments = path.Split('.');
        string typing = segments[^1];
        int replaceFrom = position - typing.Length;

        INamedTypeSymbol? within = symbols.ClassOf(context);
        Dictionary<string, string> named = MarkupScanner.NamedElements(text);

        var items = new List<CompletionEntry>();
        ITypeSymbol? source;
        IEnumerable<string> steps;

        if (segments.Length > 1 && named.TryGetValue(segments[0], out string? elementName))
        {
            source = symbols.TypeOf(elementName, context);
            steps = segments.Skip(1).SkipLast(1);
        }
        else
        {
            source = symbols.DataTypeOf(context);
            steps = segments.SkipLast(1);

            // the first step can be a named element too
            if (segments.Length == 1)
            {
                foreach ((string name, string element) in named)
                    items.Add(new CompletionEntry(name, CompletionKind.Field, element + " — a named element", Rank: 2));
            }
        }

        if (source is not null && symbols.Walk(source, steps, within) is { } owner)
        {
            foreach (IPropertySymbol property in symbols.PathProperties(owner, within))
                items.Add(new CompletionEntry(property.Name, CompletionKind.Property, SymbolText.Detail(property), Documentation: SymbolText.Summary(property)));
        }

        return (items, replaceFrom);
    }
}
