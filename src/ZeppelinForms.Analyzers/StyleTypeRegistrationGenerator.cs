using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ZeppelinForms.Analyzers;

/// <summary>
/// Lists the controls of an assembly for the style system: a module initializer that
/// hands every type derived from UIElement to <c>StyleTypeRegistry.Register</c>,
/// together with a callback that runs their static initializers.
/// </summary>
/// <remarks>
/// <para>
/// A style sheet names controls by their short names, and the style system has to know
/// which exist. It used to enumerate the types of every loaded assembly — impossible to
/// keep under trimming, and native AOT doesn't list types compiled without metadata.
/// Named with <c>typeof</c> here, the controls are kept, and their static initializers —
/// which register the styled properties a sheet checks its declarations against — are
/// reachable by the trimmer.
/// </para>
/// <para>
/// Only what the assembly's own code can name is listed: private and protected nested
/// types and generic ones are left out. A sheet can't usefully name those anyway.
/// </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class StyleTypeRegistrationGenerator : IIncrementalGenerator
{
    private const string ElementMetadataName = "ZeppelinForms.Forms.Controls.Base.UIElement";
    private const string RegistryMetadataName = "ZeppelinForms.Forms.Styling.StyleTypeRegistry";

    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat;

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<string> types = context.SyntaxProvider
            .CreateSyntaxProvider(
                // a class with a base, or UIElement itself, which may have none
                static (node, _) => node is ClassDeclarationSyntax { BaseList: not null } or
                    ClassDeclarationSyntax { Identifier.ValueText: "UIElement" },
                static (ctx, cancel) => Read(ctx, cancel))
            .Where(static name => name is not null)!;

        IncrementalValueProvider<bool> canRegister = context.CompilationProvider
            .Select(static (compilation, _) =>
                compilation.GetTypeByMetadataName(ElementMetadataName) is not null &&
                compilation.GetTypeByMetadataName(RegistryMetadataName) is { } registry &&
                compilation.IsSymbolAccessibleWithin(registry, compilation.Assembly));

        context.RegisterSourceOutput(types.Collect().Combine(canRegister), static (output, input) =>
        {
            var (names, ok) = input;

            if (!ok || names.IsDefaultOrEmpty) return;

            output.AddSource("StyleTypeRegistration.g.cs", SourceText.From(Emit(names), Encoding.UTF8));
        });
    }

    /// <summary>The fully qualified name of a class that is or derives from UIElement and
    /// that its assembly can name, or null.</summary>
    private static string? Read(GeneratorSyntaxContext context, CancellationToken cancel)
    {
        if (context.SemanticModel.GetDeclaredSymbol(context.Node, cancel) is not INamedTypeSymbol type) return null;

        if (type.IsGenericType || !IsNameable(type)) return null;

        INamedTypeSymbol? element = context.SemanticModel.Compilation.GetTypeByMetadataName(ElementMetadataName);
        if (element is null) return null;

        // UIElement itself too, in the framework's own assembly: its static initializer
        // registers the properties every control has — Padding, TextColor, CornerRadius
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
            if (SymbolEqualityComparer.Default.Equals(current, element))
                return type.ToDisplayString(TypeFormat);

        return null;
    }

    /// <summary>Whether code elsewhere in the assembly can write <c>typeof</c> of it:
    /// it and every type around it are public or internal, none of them generic.</summary>
    private static bool IsNameable(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            if (current.IsGenericType) return false;

            switch (current.DeclaredAccessibility)
            {
                case Accessibility.Public:
                case Accessibility.Internal:
                case Accessibility.ProtectedOrInternal:
                    break;

                default:
                    return false;
            }

            // a file-local type can't be named outside its file
            if (current.IsFileLocal) return false;
        }

        return true;
    }

    private static string Emit(ImmutableArray<string> names)
    {
        // a partial class has a declaration per part: each type once, in a stable order
        string[] types = [.. names.Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal)];

        var source = new StringBuilder();

        source.AppendLine("// <auto-generated/>");
        source.AppendLine("#nullable enable");
        source.AppendLine();
        source.AppendLine("namespace ZeppelinForms.Generated");
        source.AppendLine("{");
        source.AppendLine("    /// <summary>The controls of this assembly, for the style sheets that name them.</summary>");
        source.AppendLine("    [global::System.CodeDom.Compiler.GeneratedCode(\"ZeppelinForms.Analyzers\", null)]");
        source.AppendLine("    internal static class StyleTypeRegistration");
        source.AppendLine("    {");
        source.AppendLine("        [global::System.Runtime.CompilerServices.ModuleInitializer]");
        source.AppendLine("        internal static void Register() =>");
        source.AppendLine("            global::ZeppelinForms.Forms.Styling.StyleTypeRegistry.Register(");
        source.AppendLine("                new global::System.Type[]");
        source.AppendLine("                {");

        foreach (string type in types)
            source.Append("                    typeof(").Append(type).AppendLine("),");

        source.AppendLine("                },");
        source.AppendLine("                static () =>");
        source.AppendLine("                {");

        foreach (string type in types)
            source.Append("                    global::System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(")
                .Append(type).AppendLine(").TypeHandle);");

        source.AppendLine("                });");
        source.AppendLine("    }");
        source.AppendLine("}");

        return source.ToString();
    }
}
