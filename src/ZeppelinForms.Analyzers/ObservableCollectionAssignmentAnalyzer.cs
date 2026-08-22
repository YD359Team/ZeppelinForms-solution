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
        title: "Присваивание новой ObservableCollection теряет подписчиков",
        messageFormat: "Присваивание нового значения '{0}' заменяет существующий экземпляр " +
                        "ObservableCollection и отвязывает все обработчики CollectionChanged. " +
                        "Используйте '{0}.Add(...)' или инициализатор '{{ }}' вместо '='.",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // "цепляемся" за любое присваивание вида X = Y в синтаксическом дереве —
        // это дёшево (чистый синтаксис, без семантики), дальше уже фильтруем по смыслу
        context.RegisterSyntaxNodeAction(AnalyzeAssignment, SyntaxKind.SimpleAssignmentExpression);
    }

    private static void AnalyzeAssignment(SyntaxNodeAnalysisContext context)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;

        // "Foo = { a, b }" — это НЕ присваивание нового экземпляра, компилятор
        // разворачивает это в вызовы Add() на существующей коллекции. Пропускаем.
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