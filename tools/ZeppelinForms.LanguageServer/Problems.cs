using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using ZeppelinForms.Analyzers.Markup;
using ZeppelinForms.LanguageServer.Protocol;
using ZeppelinForms.LanguageServer.Workspace;

namespace ZeppelinForms.LanguageServer;

/// <summary>
/// The problems of a document, as the build would report them: the markup generator's
/// ZF2xxx and the compiler's errors in the code it wrote, mapped back to the markup's lines;
/// for a style sheet, its shape and the controls and properties it names.
/// </summary>
internal static class Problems
{
    public static JsonArray Of(string path, string text, ProjectSnapshot snapshot)
    {
        var problems = new JsonArray();

        if (path.EndsWith(".zfml", StringComparison.OrdinalIgnoreCase))
        {
            foreach (Diagnostic diagnostic in snapshot.Diagnostics)
                if (IsAt(diagnostic.Location.GetLineSpan(), path))
                    problems.Add(ToJson(diagnostic, diagnostic.Location.GetLineSpan()));

            // the generated code's own errors — a handler of the wrong signature — at the
            // markup's line its #line points to
            string hint = Path.GetFileName(path) + ".g.cs";

            foreach (SyntaxTree tree in snapshot.Output.SyntaxTrees.Where(t => t.FilePath.EndsWith(hint, StringComparison.OrdinalIgnoreCase)))
            {
                foreach (Diagnostic diagnostic in snapshot.Output.GetSemanticModel(tree).GetDiagnostics())
                {
                    FileLinePositionSpan mapped = diagnostic.Location.GetMappedLineSpan();

                    if (diagnostic.Severity == DiagnosticSeverity.Error && IsAt(mapped, path))
                        problems.Add(ToJson(diagnostic, mapped));
                }
            }
        }
        else if (path.EndsWith(".zss", StringComparison.OrdinalIgnoreCase) && snapshot.Element is { } element)
        {
            StyleNames names = StyleNames.Collect(snapshot.Output, element);

            foreach (StyleProblem problem in InlineStyleChecker.Check(text, names))
            {
                int end = problem.Offset;
                while (end < text.Length && !char.IsWhiteSpace(text[end]) && text[end] is not (';' or '{' or '}')) end++;

                problems.Add(new JsonObject
                {
                    ["range"] = TextPosition.Range(text, problem.Offset, Math.Max(end, problem.Offset + 1)),
                    ["severity"] = problem.IsError ? 1 : 2,
                    ["source"] = "zeppelinforms",
                    ["message"] = problem.Message,
                });
            }
        }

        return problems;
    }

    private static bool IsAt(FileLinePositionSpan span, string path) =>
        span.IsValid && span.Path is { Length: > 0 } where &&
        string.Equals(Path.GetFullPath(where), path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static JsonObject ToJson(Diagnostic diagnostic, FileLinePositionSpan span)
    {
        var start = span.StartLinePosition;
        var end = span.EndLinePosition;

        // a point: the markup compiler reports where an element or an attribute starts —
        // a range of one character shows where
        if (end == start) end = new Microsoft.CodeAnalysis.Text.LinePosition(start.Line, start.Character + 1);

        return new JsonObject
        {
            ["range"] = TextPosition.Range(start.Line, start.Character, end.Line, end.Character),
            ["severity"] = diagnostic.Severity switch
            {
                DiagnosticSeverity.Error => 1,
                DiagnosticSeverity.Warning => 2,
                DiagnosticSeverity.Info => 3,
                _ => 4,
            },
            ["code"] = diagnostic.Id,
            ["source"] = "zeppelinforms",
            ["message"] = diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
        };
    }
}
