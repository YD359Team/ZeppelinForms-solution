using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StyledPropertyInConstructorAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ZF0006";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Присваивание стилизуемого свойства в конструкторе закрывает его от темы",
        messageFormat: "Присваивание '{0}' в конструкторе помечает свойство заданным вручную, " +
                        "и тема его больше не изменит. Используйте " +
                        "'SetControlDefault({0}Property, ...)' — такое значение тема перекроет, " +
                        "а код пользователя тем более.",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeAssignment, SyntaxKind.SimpleAssignmentExpression);
    }

    private static void AnalyzeAssignment(SyntaxNodeAnalysisContext context)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;

        // интересуют только конструкторы: в обычных методах присваивание
        // стилизуемого свойства — это как раз намеренная запись от кода
        if (assignment.FirstAncestorOrSelf<ConstructorDeclarationSyntax>() is null)
            return;

        // "new Label { Padding = ... }" внутри конструктора — это настройка
        // другого объекта, а не своих умолчаний
        if (assignment.Parent is InitializerExpressionSyntax)
            return;

        // присваивание себе: "X = ..." или "this.X = ...".
        // "child.X = ..." трогает чужой элемент и нас не касается
        if (!IsAssignmentToSelf(assignment.Left))
            return;

        if (context.SemanticModel.GetSymbolInfo(assignment.Left, context.CancellationToken)
                .Symbol is not IPropertySymbol property)
            return;

        if (!HasStyledAttribute(property))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule, assignment.GetLocation(), property.Name));
    }

    private static bool IsAssignmentToSelf(ExpressionSyntax left) => left switch
    {
        IdentifierNameSyntax => true,
        MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } => true,
        _ => false,
    };

    private static bool HasStyledAttribute(IPropertySymbol property)
    {
        // Свойство объявлено partial, и атрибут лежит на объявлении в исходнике.
        // Ищем по имени класса атрибута: так правило не зависит от того,
        // в каком пространстве имён он объявлен
        foreach (AttributeData attribute in property.GetAttributes())
        {
            if (attribute.AttributeClass?.Name is "StyledAttribute" or "Styled")
                return true;
        }

        // свойство могло быть переопределено — атрибут стоит на базовом
        for (IPropertySymbol? current = property.OverriddenProperty;
             current is not null;
             current = current.OverriddenProperty)
        {
            foreach (AttributeData attribute in current.GetAttributes())
            {
                if (attribute.AttributeClass?.Name is "StyledAttribute" or "Styled")
                    return true;
            }
        }

        return false;
    }
}