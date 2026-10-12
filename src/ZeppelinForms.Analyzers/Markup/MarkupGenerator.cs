using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ZeppelinForms.Analyzers.Markup;

/// <summary>
/// Compiles <c>.zfml</c> markup into C#: for <c>x:Class="App.MainForm"</c> a part of
/// <c>partial class MainForm</c> with a field for every <c>x:Name</c> and an
/// <c>InitializeComponent()</c> that builds the tree with plain <c>new</c>, assignments,
/// <c>+=</c> and <c>Add</c>.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is left for run time: types, properties, events and values are resolved
/// here against the compilation, and what doesn't resolve is a ZF2xxx error at its
/// line of the markup. The generated code carries <c>#line</c> directives to the
/// markup, so an error the compiler finds in it — a handler of the wrong signature —
/// points there too. No reflection, so trimming and native AOT keep everything.
/// </para>
/// <para>
/// The markup files reach the generator as AdditionalFiles; the package's build
/// targets add every <c>*.zfml</c> of a project.
/// </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class MarkupGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<(string Path, string? Text)> files = context.AdditionalTextsProvider
            .Where(static file => file.Path.EndsWith(".zfml", StringComparison.OrdinalIgnoreCase))
            .Select(static (file, cancel) => (file.Path, file.GetText(cancel)?.ToString()));

        context.RegisterSourceOutput(files.Combine(context.CompilationProvider), static (output, input) =>
        {
            var ((path, text), compilation) = input;

            if (text is null) return;

            MarkupResult result = MarkupCompiler.Compile(path, text, compilation);

            foreach (Diagnostic diagnostic in result.Diagnostics)
                output.ReportDiagnostic(diagnostic);

            if (result.Source is not null)
                output.AddSource(result.HintName, SourceText.From(result.Source, System.Text.Encoding.UTF8));
        });
    }
}

/// <summary>What a markup file compiles to: the source, or the reasons it couldn't.</summary>
internal sealed class MarkupResult(string hintName, string? source, IReadOnlyList<Diagnostic> diagnostics)
{
    public string HintName { get; } = hintName;

    public string? Source { get; } = source;

    public IReadOnlyList<Diagnostic> Diagnostics { get; } = diagnostics;
}

/// <summary>The markup's errors. Each points at its line of the .zfml file.</summary>
internal static class MarkupDiagnostics
{
    private const string Category = "Markup";

    public static readonly DiagnosticDescriptor Unreadable = Error(
        "ZF2001", "The markup is not well-formed XML", "The markup can't be read: {0}");

    public static readonly DiagnosticDescriptor NoClass = Error(
        "ZF2002", "The markup names no class",
        "The root element needs x:Class=\"Namespace.Name\": the class the markup builds");

    public static readonly DiagnosticDescriptor UnknownType = Error(
        "ZF2003", "Unknown element", "There is no type '{0}'{1}");

    public static readonly DiagnosticDescriptor UnknownMember = Error(
        "ZF2004", "Unknown property or event", "'{0}' has no settable property or event '{1}'");

    public static readonly DiagnosticDescriptor BadValue = Error(
        "ZF2005", "A value can't be written", "'{0}' can't be written as {1}: {2}");

    public static readonly DiagnosticDescriptor NoPlace = Error(
        "ZF2006", "A child has nowhere to go", "'{0}' can't hold a {1}: {2}");

    public static readonly DiagnosticDescriptor NoHandler = Error(
        "ZF2007", "No handler for an event", "'{0}' has no method '{1}' to handle {2}");

    public static readonly DiagnosticDescriptor BadBinding = Error(
        "ZF2008", "A binding can't be made", "{0}");

    public static readonly DiagnosticDescriptor DuplicateName = Error(
        "ZF2009", "A name is used twice", "x:Name '{0}' is already given to another element");

    public static readonly DiagnosticDescriptor NotCreatable = Error(
        "ZF2010", "An element can't be created", "'{0}' has no public constructor without parameters");

    private static DiagnosticDescriptor Error(string id, string title, string message) =>
        new(id, title, message, Category, DiagnosticSeverity.Error, isEnabledByDefault: true);
}
