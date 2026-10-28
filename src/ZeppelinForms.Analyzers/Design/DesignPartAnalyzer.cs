using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeppelinForms.Analyzers.Design;

/// <summary>
/// ZF0007: a constructor of a class with a design part that doesn't build it. The form
/// then opens empty — and its fields are null, which the first handler finds.
/// </summary>
/// <remarks>
/// A constructor that hands over to another — <c>: this(…)</c> — is fine: the other one is
/// looked at. A call through a method of one's own isn't followed: such a constructor
/// silences the warning with a <c>#pragma</c>, or better calls <c>InitializeComponent()</c>
/// where it can be seen.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DesignPartAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ZF0007";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "A constructor doesn't build the design part",
        messageFormat: "The constructor of '{0}' doesn't call InitializeComponent(): what {1} describes isn't built, " +
                       "and its fields stay null",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeConstructor, SyntaxKind.ConstructorDeclaration);
    }

    private static void AnalyzeConstructor(SyntaxNodeAnalysisContext context)
    {
        var constructor = (ConstructorDeclarationSyntax)context.Node;

        if (constructor.Modifiers.Any(SyntaxKind.StaticKeyword)) return;

        // a constructor without a body — extern, or a partial one's declaration — builds nothing to look at
        if (constructor.Body is null && constructor.ExpressionBody is null) return;

        if (DesignParts.BuildsDesign(constructor)) return;

        if (context.SemanticModel.GetDeclaredSymbol(constructor, context.CancellationToken) is not { ContainingType: { } type })
            return;

        if (DesignParts.DesignMethod(type) is not { } design) return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule, constructor.Identifier.GetLocation(), type.Name, DesignParts.DescribePart(design)));
    }
}
