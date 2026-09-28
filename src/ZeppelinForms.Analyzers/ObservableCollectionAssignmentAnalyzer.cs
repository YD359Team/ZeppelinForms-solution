using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;

namespace ZeppelinForms.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ObservableCollectionAssignmentAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ZF0001";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Assigning a new ObservableCollection loses its subscribers",
        messageFormat: "Assigning a new value to '{0}' replaces the existing " +
                        "ObservableCollection instance and detaches all CollectionChanged handlers. " +
                        "Use '{0}.Add(...)' or a '{{ }}' initializer instead of '='.",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // we hook onto any assignment of the form X = Y in the syntax tree —
        // it's cheap (pure syntax, no semantics), the meaning is filtered after that
        context.RegisterSyntaxNodeAction(AnalyzeAssignment, SyntaxKind.SimpleAssignmentExpression);
    }

    private static void AnalyzeAssignment(SyntaxNodeAnalysisContext context)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;

        // "Foo = { a, b }" is NOT an assignment of a new instance: the compiler
        // expands it into Add() calls on the existing collection. Skipped.
        if (assignment.Right is InitializerExpressionSyntax)
            return;

        ITypeSymbol? targetType = context.SemanticModel
            .GetTypeInfo(assignment.Left, context.CancellationToken).Type;

        if (targetType is null)
            return;

        INamedTypeSymbol? observableCollectionType = context.Compilation
            .GetTypeByMetadataName("System.Collections.ObjectModel.ObservableCollection`1");

        if (observableCollectionType is null || !InheritsFrom(targetType, observableCollectionType))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule, assignment.GetLocation(), assignment.Left.ToString()));
    }

    private static bool InheritsFrom(ITypeSymbol type, INamedTypeSymbol baseType)
    {
        for (ITypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (current is INamedTypeSymbol named &&
                SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, baseType))
                return true;
        }
        return false;
    }
}