using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Utilities;
using Newtonsoft.Json.Linq;

namespace ZeppelinForms.VisualStudio.LanguageServer;

/// <summary>Quick info in .zfml from the language server: an element's type, an attribute's
/// property or event, a step of a binding's path — the declaration and the summary.</summary>
[Export(typeof(IAsyncQuickInfoSourceProvider))]
[Name("ZeppelinForms markup quick info")]
[ContentType("text")]
internal sealed class ZfQuickInfoSourceProvider : IAsyncQuickInfoSourceProvider
{
    public IAsyncQuickInfoSource? TryCreateQuickInfoSource(ITextBuffer textBuffer) =>
        ZfLanguageServer.FileOf(textBuffer) is { } path && path.EndsWith(".zfml", StringComparison.OrdinalIgnoreCase)
            ? textBuffer.Properties.GetOrCreateSingletonProperty(() => new ZfQuickInfoSource(textBuffer))
            : null;
}

internal sealed class ZfQuickInfoSource(ITextBuffer buffer) : IAsyncQuickInfoSource
{
    public async Task<QuickInfoItem?> GetQuickInfoItemAsync(IAsyncQuickInfoSession session, CancellationToken cancellationToken)
    {
        ITextSnapshot snapshot = buffer.CurrentSnapshot;

        if (session.GetTriggerPoint(snapshot) is not { } point ||
            ZfLanguageServer.FileOf(buffer) is not { } path ||
            ZfLanguageServer.Instance is not { } server)
        {
            return null;
        }

        ITextSnapshotLine line = point.GetContainingLine();

        JToken? hover = await server.HoverAsync(path, snapshot.GetText(), line.LineNumber, point.Position - line.Start.Position, cancellationToken);

        if ((string?)hover?["contents"]?["value"] is not { Length: > 0 } markdown) return null;

        Span span = new(point.Position, 0);

        if (hover!["range"] is { } range)
        {
            int start = snapshot.GetLineFromLineNumber((int)range["start"]!["line"]!).Start.Position + (int)range["start"]!["character"]!;
            int end = snapshot.GetLineFromLineNumber((int)range["end"]!["line"]!).Start.Position + (int)range["end"]!["character"]!;
            span = Span.FromBounds(start, Math.Max(start, end));
        }

        return new QuickInfoItem(snapshot.CreateTrackingSpan(span, SpanTrackingMode.EdgeInclusive), Render(markdown));
    }

    /// <summary>The server's markdown — a fenced declaration, then the summary — as the
    /// tooltip's two parts.</summary>
    private static object Render(string markdown)
    {
        string declaration = markdown;
        string? summary = null;

        if (markdown.StartsWith("```", StringComparison.Ordinal))
        {
            int bodyStart = markdown.IndexOf('\n') + 1;
            int fenceEnd = markdown.IndexOf("```", bodyStart, StringComparison.Ordinal);

            if (bodyStart > 0 && fenceEnd > bodyStart)
            {
                declaration = markdown.Substring(bodyStart, fenceEnd - bodyStart).Trim();
                summary = markdown.Substring(fenceEnd + 3).Trim();
            }
        }

        var parts = new List<object> { new ClassifiedTextElement(new ClassifiedTextRun("text", declaration)) };

        if (summary is { Length: > 0 })
            parts.Add(new ClassifiedTextElement(new ClassifiedTextRun("text", summary.Replace("`", string.Empty))));

        return new ContainerElement(ContainerElementStyle.Stacked, parts);
    }

    public void Dispose()
    {
    }
}
