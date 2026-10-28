using Microsoft.CodeAnalysis;
using CodeAccessibility = Microsoft.CodeAnalysis.Accessibility;
using ZeppelinForms.LanguageServer.Workspace;

namespace ZeppelinForms.LanguageServer.Markup;

/// <summary>The names of markup as the generator resolves them: the framework's types by
/// their short names, the application's through <c>using:</c> namespaces, the members an
/// attribute can name.</summary>
internal sealed class MarkupSymbols
{
    public const string DefaultNamespace = "https://zeppelinforms.dev/markup";
    public const string XNamespace = "https://zeppelinforms.dev/markup/x";
    public const string CommandMetadataName = "System.Windows.Input.ICommand";

    private readonly Dictionary<string, INamedTypeSymbol?> _framework = new(StringComparer.Ordinal);

    public MarkupSymbols(ProjectSnapshot snapshot)
    {
        Snapshot = snapshot;
        Compilation = snapshot.Output;
        Element = snapshot.Element;

        if (Element is not null)
            CollectFramework(Element.ContainingAssembly);
    }

    public ProjectSnapshot Snapshot { get; }

    public Compilation Compilation { get; }

    /// <summary>UIElement; null — no framework referenced.</summary>
    public INamedTypeSymbol? Element { get; }

    /// <summary>The framework's types a markup element can name: public classes, a name two of
    /// them share going to the control, as the generator decides.</summary>
    public IEnumerable<INamedTypeSymbol> FrameworkTypes => _framework.Values.OfType<INamedTypeSymbol>();

    private void CollectFramework(IAssemblySymbol assembly)
    {
        foreach (INamedTypeSymbol type in AllTypes(assembly.GlobalNamespace))
        {
            if (type.DeclaredAccessibility != CodeAccessibility.Public || type.IsGenericType || type.IsStatic) continue;
            if (type.TypeKind != TypeKind.Class) continue;

            if (_framework.TryGetValue(type.Name, out INamedTypeSymbol? existing))
            {
                bool existingIsElement = existing is not null && IsElement(existing);
                bool thisIsElement = IsElement(type);

                _framework[type.Name] = existingIsElement == thisIsElement ? null : existingIsElement ? existing : type;
            }
            else
            {
                _framework[type.Name] = type;
            }
        }
    }

    public static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceSymbol root)
    {
        var pending = new Stack<INamespaceSymbol>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            INamespaceSymbol current = pending.Pop();

            foreach (INamespaceSymbol inner in current.GetNamespaceMembers())
                pending.Push(inner);

            foreach (INamedTypeSymbol type in current.GetTypeMembers())
                yield return type;
        }
    }

    public bool IsElement(ITypeSymbol type) => Element is not null && Inherits(type, Element);

    public static bool Inherits(ITypeSymbol type, INamedTypeSymbol @base)
    {
        for (ITypeSymbol? current = type; current is not null; current = current.BaseType)
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, @base.OriginalDefinition))
                return true;

        return false;
    }

    /// <summary>A type markup can create: a class with a public constructor without parameters.</summary>
    public static bool IsCreatable(INamedTypeSymbol type) =>
        type.TypeKind == TypeKind.Class && !type.IsAbstract && !type.IsStatic &&
        type.InstanceConstructors.Any(c => c.Parameters.Length == 0 && c.DeclaredAccessibility == CodeAccessibility.Public);

    /// <summary>The type of an element's name: <c>Button</c>, <c>vm:Card</c>; for a property
    /// element <c>Grid.Styles</c>, its owner's.</summary>
    public INamedTypeSymbol? TypeOf(string written, MarkupContext context)
    {
        int dot = written.IndexOf('.');
        if (dot > 0) written = written[..dot];

        int colon = written.IndexOf(':');

        if (colon < 0)
            return _framework.TryGetValue(written, out INamedTypeSymbol? type) ? type : null;

        string prefix = written[..colon];
        string local = written[(colon + 1)..];

        if (prefix == "x") return local == "String" ? Compilation.GetSpecialType(SpecialType.System_String) : null;

        return NamespaceOfPrefix(prefix, context) is { } ns ? Compilation.GetTypeByMetadataName(ns + "." + local) : null;
    }

    /// <summary>The CLR namespace a prefix stands for: <c>using:App.Controls</c>.</summary>
    public static string? NamespaceOfPrefix(string prefix, MarkupContext context) => ClrNamespace(context.NamespaceOf(prefix));

    public static string? ClrNamespace(string? xmlns) =>
        xmlns is null ? null
        : xmlns.StartsWith("using:", StringComparison.Ordinal) ? xmlns[6..]
        : xmlns.StartsWith("clr-namespace:", StringComparison.Ordinal) ? xmlns[14..].Split(';')[0]
        : null;

    /// <summary>The types of a CLR namespace, as the compilation has them.</summary>
    public IEnumerable<INamedTypeSymbol> TypesIn(string ns)
    {
        INamespaceSymbol? current = Compilation.GlobalNamespace;

        foreach (string part in ns.Split('.'))
        {
            current = current?.GetNamespaceMembers().FirstOrDefault(n => n.Name == part);
            if (current is null) return [];
        }

        return current!.GetTypeMembers().Where(t => t.DeclaredAccessibility is CodeAccessibility.Public or CodeAccessibility.Internal);
    }

    /// <summary>The class the markup builds: Class or x:Class of the root.</summary>
    public INamedTypeSymbol? ClassOf(MarkupContext context)
    {
        MarkupElement? root = context.Ancestors.Count > 0 ? context.Ancestors[0] : context.Tag;
        if (root is null) return null;

        string? name = root.Attributes.TryGetValue("x:Class", out string? x) ? x
            : root.Attributes.TryGetValue("Class", out string? plain) ? plain : null;

        return name is null ? null : Compilation.GetTypeByMetadataName(name.Trim());
    }

    /// <summary>What x:DataType names: prefix:Type, a type of the class's namespace or of the
    /// markup's using: namespaces, or a full name.</summary>
    public INamedTypeSymbol? DataTypeOf(MarkupContext context)
    {
        if (context.DataType is not { } written) return null;

        written = written.Trim();
        int colon = written.IndexOf(':');

        if (colon > 0)
            return NamespaceOfPrefix(written[..colon], context) is { } ns
                ? Compilation.GetTypeByMetadataName(ns + "." + written[(colon + 1)..])
                : null;

        if (ClassOf(context)?.ContainingNamespace is { IsGlobalNamespace: false } classNamespace &&
            Compilation.GetTypeByMetadataName(classNamespace.ToDisplayString() + "." + written) is { } inClass)
        {
            return inClass;
        }

        foreach (MarkupElement element in context.InnermostFirst)
            foreach ((string key, string value) in element.Attributes)
                if (key.StartsWith("xmlns:", StringComparison.Ordinal) && ClrNamespace(value) is { } ns &&
                    Compilation.GetTypeByMetadataName(ns + "." + written) is { } found)
                    return found;

        return Compilation.GetTypeByMetadataName(written);
    }

    // ===== members =====

    public static IEnumerable<ISymbol> Members(ITypeSymbol type)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        IEnumerable<ITypeSymbol> chain = type.TypeKind == TypeKind.Interface
            ? new[] { type }.Concat(type.AllInterfaces)
            : Bases(type);

        foreach (ITypeSymbol current in chain)
            foreach (ISymbol member in current.GetMembers())
                if (seen.Add(member.Kind + member.Name))
                    yield return member;
    }

    private static IEnumerable<ITypeSymbol> Bases(ITypeSymbol type)
    {
        for (ITypeSymbol? current = type; current is not null; current = current.BaseType)
            yield return current;
    }

    /// <summary>What an attribute of an element can set: public instance properties, settable
    /// or collections to fill, and the public events.</summary>
    public static IEnumerable<ISymbol> AttributeMembers(ITypeSymbol type) =>
        Members(type).Where(member => member switch
        {
            IPropertySymbol { IsStatic: false, IsIndexer: false, DeclaredAccessibility: CodeAccessibility.Public } property =>
                property.SetMethod is { DeclaredAccessibility: CodeAccessibility.Public, IsInitOnly: false } || IsCollection(property.Type),
            IEventSymbol { IsStatic: false, DeclaredAccessibility: CodeAccessibility.Public } => true,
            _ => false,
        });

    /// <summary>The properties a binding path can read: instance ones with a getter the class
    /// can call.</summary>
    public IEnumerable<IPropertySymbol> PathProperties(ITypeSymbol type, INamedTypeSymbol? within) =>
        Members(type).OfType<IPropertySymbol>().Where(p =>
            !p.IsStatic && !p.IsIndexer && p.GetMethod is { } getter &&
            Compilation.IsSymbolAccessibleWithin(getter, (ISymbol?)within ?? Compilation.Assembly));

    public static bool IsCollection(ITypeSymbol type) =>
        type.AllInterfaces.Append(type as INamedTypeSymbol).Any(i =>
            i is { IsGenericType: true } generic && generic.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.ICollection<T>");

    public bool IsCommand(ITypeSymbol type)
    {
        INamedTypeSymbol? command = Compilation.GetTypeByMetadataName(CommandMetadataName);

        return command is not null &&
            (SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, command) ||
             type.AllInterfaces.Contains(command, SymbolEqualityComparer.Default));
    }

    /// <summary>The type a path ends in, from a type: <c>Customer.Name</c> → string. Null — a
    /// step isn't there.</summary>
    public ITypeSymbol? Walk(ITypeSymbol start, IEnumerable<string> steps, INamedTypeSymbol? within)
    {
        ITypeSymbol? current = start;

        foreach (string step in steps)
        {
            current = current is null ? null : PathProperties(current, within).FirstOrDefault(p => p.Name == step)?.Type;
            if (current is null) return null;
        }

        return current;
    }

    /// <summary>The styled properties of a type and its bases: <c>BackgroundProperty</c> → Background
    /// with its value type.</summary>
    public IEnumerable<(string Name, ITypeSymbol ValueType, IFieldSymbol Descriptor)> StyledProperties(ITypeSymbol type)
    {
        INamedTypeSymbol? styled = Compilation.GetTypeByMetadataName("ZeppelinForms.Forms.Styling.StyledProperty`1");
        if (styled is null) yield break;

        foreach (IFieldSymbol field in Members(type).OfType<IFieldSymbol>())
        {
            if (!field.IsStatic || field.DeclaredAccessibility != CodeAccessibility.Public || !field.Name.EndsWith("Property", StringComparison.Ordinal))
                continue;

            if (field.Type is INamedTypeSymbol fieldType && SymbolEqualityComparer.Default.Equals(fieldType.OriginalDefinition, styled))
                yield return (field.Name[..^"Property".Length], fieldType.TypeArguments[0], field);
        }
    }

    /// <summary>Every control the sheets can name: the elements of the framework, of the
    /// project and of the libraries on the framework.</summary>
    public IEnumerable<INamedTypeSymbol> Controls()
    {
        if (Element is null) yield break;

        var assemblies = new List<IAssemblySymbol> { Compilation.Assembly, Element.ContainingAssembly };

        foreach (IAssemblySymbol referenced in Compilation.SourceModule.ReferencedAssemblySymbols)
            if (referenced.Modules.Any(m => m.ReferencedAssemblies.Any(a => a.Name == Element.ContainingAssembly.Name)))
                assemblies.Add(referenced);

        foreach (IAssemblySymbol assembly in assemblies.Distinct(SymbolEqualityComparer.Default).Cast<IAssemblySymbol>())
            foreach (INamedTypeSymbol type in AllTypes(assembly.GlobalNamespace))
                if (type.TypeKind == TypeKind.Class && type.DeclaredAccessibility == CodeAccessibility.Public && IsElement(type))
                    yield return type;
    }

    /// <summary>The theme's names for <c>@Name</c>: the colors, and the theme's own members.</summary>
    public IEnumerable<IPropertySymbol> ThemeNames()
    {
        foreach (string name in new[] { "ZeppelinForms.Theming.ThemeColors", "ZeppelinForms.Theming.Theme" })
        {
            if (Compilation.GetTypeByMetadataName(name) is not { } type) continue;

            foreach (IPropertySymbol property in type.GetMembers().OfType<IPropertySymbol>())
                if (!property.IsStatic && property.DeclaredAccessibility == CodeAccessibility.Public && property.Name != "Colors" &&
                    property.Type.TypeKind != TypeKind.Delegate)
                {
                    yield return property;
                }
        }
    }
}
