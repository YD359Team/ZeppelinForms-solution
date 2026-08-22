using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;

namespace ZeppelinForms.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ShadowedOverloadAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ZF0002";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Перегрузка недостижима из-за другой перегрузки с тем же числом обязательных параметров",
        messageFormat: "Метод '{0}' с параметрами по умолчанию никогда не вызовется с {1} " +
                        "аргументами — такие вызовы всегда резолвятся в перегрузку '{2}'. " +
                        "Удалите одну из перегрузок.",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
    }

    private static void AnalyzeType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        var overloadGroups = type.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.Ordinary && !m.IsImplicitlyDeclared)
            .GroupBy(m => m.Name);

        foreach (var group in overloadGroups)
        {
            var overloads = group.ToList();
            if (overloads.Count < 2)
                continue;

            foreach (var shorter in overloads)
                foreach (var longer in overloads)
                {
                    if (!ReferenceEquals(shorter, longer) && IsShadowedBy(shorter, longer))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            Rule,
                            longer.Locations.FirstOrDefault() ?? Location.None,
                            longer.Name,
                            shorter.Parameters.Length,
                            FormatSignature(shorter)));
                    }
                }
        }
    }

    private static bool IsShadowedBy(IMethodSymbol shorter, IMethodSymbol longer)
    {
        if (shorter.Parameters.Length >= longer.Parameters.Length)
            return false;

        int n = shorter.Parameters.Length;

        for (int i = n; i < longer.Parameters.Length; i++)
            if (!longer.Parameters[i].IsOptional)
                return false;

        for (int i = 0; i < n; i++)
            if (!SymbolEqualityComparer.Default.Equals(shorter.Parameters[i].Type, longer.Parameters[i].Type))
                return false;

        return true;
    }

    private static string FormatSignature(IMethodSymbol method) =>
        $"{method.Name}({string.Join(", ", method.Parameters.Select(p => p.Type.Name))})";
}