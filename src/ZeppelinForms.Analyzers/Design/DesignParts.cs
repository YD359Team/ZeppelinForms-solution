using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZeppelinForms.Analyzers.Design;

/// <summary>
/// The design part of a class: what builds its controls, apart from what they do. Either
/// a <c>MainForm.design.cs</c> written by hand — a part of <c>partial class MainForm</c>
/// with the controls' fields and an <c>InitializeComponent()</c> that makes and lays them
/// out — or the part the markup generator compiles from <c>MainForm.zfml</c>.
/// </summary>
/// <remarks>
/// The convention is WinForms' <c>Form1.Designer.cs</c>, without a designer that owns the
/// file: it is the developer's, edited as any code. The name tells it apart, and the
/// package's build targets nest it under <c>MainForm.cs</c> in Solution Explorer.
/// </remarks>
internal static class DesignParts
{
    public const string Method = "InitializeComponent";

    public const string Extension = ".design.cs";

    /// <summary>A file of design parts: <c>*.design.cs</c>.</summary>
    public static bool IsDesignFile(string? path) =>
        path is not null && path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>The type's <c>InitializeComponent()</c> when a design part declares it —
    /// in a <c>*.design.cs</c>, or compiled from markup; null otherwise.</summary>
    public static IMethodSymbol? DesignMethod(INamedTypeSymbol type)
    {
        foreach (ISymbol member in type.GetMembers(Method))
        {
            if (member is not IMethodSymbol { IsStatic: false, Parameters.Length: 0, MethodKind: MethodKind.Ordinary } method)
                continue;

            if (method.DeclaringSyntaxReferences.Any(reference => IsDesignFile(reference.SyntaxTree.FilePath)) || IsFromMarkup(method))
                return method;
        }

        return null;
    }

    /// <summary>The design part's file name for a message: "MainForm.design.cs", or the
    /// markup's — the class name with .zfml, as the generator reads it from x:Class.</summary>
    public static string DescribePart(IMethodSymbol method) =>
        method.DeclaringSyntaxReferences
            .Select(reference => reference.SyntaxTree.FilePath)
            .FirstOrDefault(IsDesignFile) is { } path
            ? Path.GetFileName(path)
            : "its markup";

    /// <summary>Compiled by the markup generator: [GeneratedCode("ZeppelinForms.Analyzers", …)].</summary>
    /// <remarks>No list pattern here: on netstandard2.0, which the analyzers target, there
    /// is no System.Index for the compiler to take the elements with.</remarks>
    private static bool IsFromMarkup(IMethodSymbol method) =>
        method.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.ToDisplayString() == "System.CodeDom.Compiler.GeneratedCodeAttribute" &&
            attribute.ConstructorArguments.Length > 0 &&
            attribute.ConstructorArguments[0].Value is "ZeppelinForms.Analyzers");

    /// <summary>The constructor builds the design: it calls <c>InitializeComponent()</c>
    /// itself, or hands over to another constructor of the class — <c>: this(…)</c> —
    /// which is looked at on its own.</summary>
    public static bool BuildsDesign(ConstructorDeclarationSyntax constructor)
    {
        if (constructor.Initializer is { RawKind: (int)SyntaxKind.ThisConstructorInitializer })
            return true;

        SyntaxNode? body = (SyntaxNode?)constructor.Body ?? constructor.ExpressionBody;

        return body is not null && body.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(IsDesignCall);
    }

    private static bool IsDesignCall(InvocationExpressionSyntax invocation) =>
        invocation.ArgumentList.Arguments.Count == 0 && invocation.Expression switch
        {
            IdentifierNameSyntax { Identifier.ValueText: Method } => true,
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax, Name.Identifier.ValueText: Method } => true,
            _ => false,
        };

    /// <summary>The method's bodies assign the member: <c>_save = new Button();</c> or
    /// <c>this._save = …</c>, anywhere in them.</summary>
    public static bool Assigns(IMethodSymbol method, string member)
    {
        foreach (SyntaxReference reference in method.DeclaringSyntaxReferences)
        {
            foreach (AssignmentExpressionSyntax assignment in reference.GetSyntax().DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                string? target = assignment.Left switch
                {
                    IdentifierNameSyntax name => name.Identifier.ValueText,
                    MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } access => access.Name.Identifier.ValueText,
                    _ => null,
                };

                if (target == member) return true;
            }
        }

        return false;
    }
}
