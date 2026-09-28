using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;

namespace ZeppelinForms.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StyledPropertyInConstructorAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ZF0006";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Assigning a styled property in a constructor closes it to the theme",
        messageFormat: "Assigning '{0}' in a constructor marks the property as set by hand, " +
                        "and the theme will no longer change it. Use " +
                        "'SetControlDefault({0}Property, ...)' — the theme overrides such " +
                        "a value, and the user's code all the more so.",
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

        // only constructors are of interest: in ordinary methods assigning
        // a styled property is exactly an intentional write from code
        if (!RunsInConstructor(assignment))
            return;

        // "new Label { Padding = ... }" inside a constructor configures
        // another object, not our own defaults
        if (assignment.Parent is InitializerExpressionSyntax)
            return;

        // an assignment to self: "X = ..." or "this.X = ...".
        // "child.X = ..." touches another element and is none of our business
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

    /// <summary>Whether the assignment runs while the constructor runs.</summary>
    /// <remarks>
    /// The walk up stops at a lambda, an anonymous method and a local function:
    /// their body runs later. "Click += (_, _) => Background = X;" subscribed in
    /// a constructor is an ordinary write from code on a click, not the control's
    /// default, and the rule used to suggest SetControlDefault for it.
    /// </remarks>
    private static bool RunsInConstructor(SyntaxNode node)
    {
        for (SyntaxNode? current = node.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case ConstructorDeclarationSyntax:
                    return true;

                case AnonymousFunctionExpressionSyntax:
                case LocalFunctionStatementSyntax:
                case MemberDeclarationSyntax:
                    return false;
            }
        }

        return false;
    }

    private static bool IsAssignmentToSelf(ExpressionSyntax left) => left switch
    {
        IdentifierNameSyntax => true,
        MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } => true,
        _ => false,
    };

    private static bool HasStyledAttribute(IPropertySymbol property)
    {
        // The property is declared partial, and the attribute lies on the declaration
        // in the source. We look it up by the attribute class name: this way the rule
        // doesn't depend on which namespace it is declared in
        foreach (AttributeData attribute in property.GetAttributes())
        {
            if (attribute.AttributeClass?.Name is "StyledAttribute" or "Styled")
                return true;
        }

        // the property may have been overridden — the attribute is on the base one
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