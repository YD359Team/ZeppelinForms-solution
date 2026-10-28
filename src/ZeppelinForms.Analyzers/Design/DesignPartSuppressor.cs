using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeppelinForms.Analyzers.Design;

/// <summary>
/// CS8618 is not true of a field a design part makes: <c>InitializeComponent()</c> assigns
/// <c>_save</c>, the constructor calls it, and the field is set when the constructor ends —
/// the compiler only doesn't look into the call. So <c>private Button _save;</c> of a
/// <c>.design.cs</c> needs no <c>= null!</c>.
/// </summary>
/// <remarks>
/// Suppressed only where all three hold: the member is declared in a <c>*.design.cs</c>,
/// the type's <c>InitializeComponent()</c> there assigns it, and the constructor the
/// warning is about calls <c>InitializeComponent()</c>. A field the method forgets, or a
/// constructor that doesn't build the design, keeps the warning.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DesignPartSuppressor : DiagnosticSuppressor
{
    private const string UninitializedNonNullable = "CS8618";

    private static readonly SuppressionDescriptor Suppression = new(
        id: "ZFSPR0001",
        suppressedDiagnosticId: UninitializedNonNullable,
        justification: "The field is assigned by InitializeComponent() of the design part, which the constructor calls");

    /// <summary>The member's name: the first quoted word of the message, read in the
    /// invariant culture — "Non-nullable field '_save' must contain a non-null value…".</summary>
    private static readonly Regex QuotedName = new("'([^']+)'", RegexOptions.CultureInvariant);

    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions => ImmutableArray.Create(Suppression);

    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        foreach (Diagnostic diagnostic in context.ReportedDiagnostics)
        {
            if (diagnostic.Location.SourceTree is not { } tree) continue;

            SyntaxNode node = tree.GetRoot(context.CancellationToken).FindNode(diagnostic.Location.SourceSpan);

            // reported at the constructor that leaves the member unset
            if (node.FirstAncestorOrSelf<ConstructorDeclarationSyntax>() is not { } constructor) continue;
            if (!DesignParts.BuildsDesign(constructor)) continue;

            if (context.GetSemanticModel(tree).GetDeclaredSymbol(constructor, context.CancellationToken) is not { ContainingType: { } type })
                continue;

            if (DesignParts.DesignMethod(type) is not { } design) continue;

            Match name = QuotedName.Match(diagnostic.GetMessage(CultureInfo.InvariantCulture));
            if (!name.Success) continue;

            string member = name.Groups[1].Value;

            bool declaredInDesign = type.GetMembers(member).Any(symbol =>
                symbol is IFieldSymbol or IPropertySymbol &&
                symbol.DeclaringSyntaxReferences.Any(reference => DesignParts.IsDesignFile(reference.SyntaxTree.FilePath)));

            if (declaredInDesign && DesignParts.Assigns(design, member))
                context.ReportSuppression(Microsoft.CodeAnalysis.Diagnostics.Suppression.Create(Suppression, diagnostic));
        }
    }
}
