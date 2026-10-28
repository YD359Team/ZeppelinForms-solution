using System.Collections.Immutable;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using Newtonsoft.Json.Linq;

namespace ZeppelinForms.VisualStudio.LanguageServer;

/// <summary>Completion in .zfml and .zss from the language server: in the XML editor the
/// markup opens in, and in the editor of the sheets. Any other document — "text" is every
/// document's base — is left to its own sources.</summary>
[Export(typeof(IAsyncCompletionSourceProvider))]
[Name("ZeppelinForms markup and style sheet completion")]
[ContentType("text")]
internal sealed class ZfCompletionSourceProvider : IAsyncCompletionSourceProvider
{
    public IAsyncCompletionSource GetOrCreate(ITextView textView) =>
        textView.Properties.GetOrCreateSingletonProperty(() => new ZfCompletionSource());
}

internal sealed class ZfCompletionSource : IAsyncCompletionSource
{
    /// <summary>The characters after which the server has something to offer: a tag's start,
    /// a space in a tag, a prefix, a quote, a path's dot, a binding's brace, a theme color.</summary>
    private const string TriggerCharacters = "<: .\"{@$,&#";

    /// <summary>The properties of an item: what it inserts, where the caret goes, its description.</summary>
    internal const string InsertKey = "zf.insert";
    internal const string CaretKey = "zf.caret";
    private const string DescriptionKey = "zf.description";

    public CompletionStartData InitializeCompletion(CompletionTrigger trigger, SnapshotPoint triggerLocation, CancellationToken token)
    {
        if (ZfLanguageServer.FileOf(triggerLocation.Snapshot.TextBuffer) is null)
            return CompletionStartData.DoesNotParticipateInCompletion;

        if (trigger.Reason == CompletionTriggerReason.Insertion &&
            !IsWordCharacter(trigger.Character) && TriggerCharacters.IndexOf(trigger.Character) < 0)
        {
            return CompletionStartData.DoesNotParticipateInCompletion;
        }

        // the word before the caret is what the list filters by and the choice replaces
        ITextSnapshot snapshot = triggerLocation.Snapshot;
        int start = triggerLocation.Position;

        while (start > 0 && IsWordCharacter(snapshot[start - 1])) start--;

        return new CompletionStartData(CompletionParticipation.ProvidesItems,
            new SnapshotSpan(snapshot, start, triggerLocation.Position - start));
    }

    private static bool IsWordCharacter(char c) => char.IsLetterOrDigit(c) || c is '_' or '-';

    public async Task<CompletionContext> GetCompletionContextAsync(
        IAsyncCompletionSession session, CompletionTrigger trigger, SnapshotPoint triggerLocation, SnapshotSpan applicableToSpan, CancellationToken token)
    {
        ITextSnapshot snapshot = triggerLocation.Snapshot;

        if (ZfLanguageServer.FileOf(snapshot.TextBuffer) is not { } path || ZfLanguageServer.Instance is not { } server)
            return CompletionContext.Empty;

        ITextSnapshotLine line = triggerLocation.GetContainingLine();
        string text = snapshot.GetText();

        JToken? list = await server.CompletionAsync(path, text, line.LineNumber, triggerLocation.Position - line.Start.Position, token);

        if (list?["items"] is not JArray items || items.Count == 0)
            return CompletionContext.Empty;

        var result = ImmutableArray.CreateBuilder<CompletionItem>(items.Count);

        foreach (JToken entry in items)
        {
            string label = (string?)entry["label"] ?? string.Empty;
            string insert = (string?)entry["textEdit"]?["newText"] ?? label;

            // the server replaces from where it reads the word to start — "Grid." of
            // "Grid.Styles" — the editor from where its word starts: the difference is
            // left out of what goes in, or added to it
            if (entry["textEdit"]?["range"]?["start"] is { } from)
            {
                int serverStart = snapshot.GetLineFromLineNumber((int)from["line"]!).Start.Position + (int)from["character"]!;
                int editorStart = applicableToSpan.Start.Position;

                if (serverStart < editorStart)
                {
                    string typed = snapshot.GetText(serverStart, editorStart - serverStart);
                    if (insert.StartsWith(typed, StringComparison.Ordinal)) insert = insert.Substring(typed.Length);
                }
                else if (serverStart > editorStart)
                {
                    insert = snapshot.GetText(editorStart, serverStart - editorStart) + insert;
                }
            }

            // a snippet's $1 is where the caret goes; the editor has no snippets here
            int caret = insert.IndexOf("$1", StringComparison.Ordinal);
            insert = insert.Replace("$1", string.Empty).Replace("$0", string.Empty);
            if (caret < 0) caret = insert.Length;

            // display text, source, icon, filters, suffix, insert text, sort text, filter text, attribute icons
            var item = new CompletionItem(
                label, this, null!, ImmutableArray<CompletionFilter>.Empty, string.Empty,
                insert, (string?)entry["sortText"] ?? label, FilterText(label),
                ImmutableArray<ImageElement>.Empty);

            item.Properties.AddProperty(InsertKey, insert);
            item.Properties.AddProperty(CaretKey, caret);
            item.Properties.AddProperty(DescriptionKey, Description(entry));

            result.Add(item);
        }

        return new CompletionContext(result.MoveToImmutable());
    }

    /// <summary>What the list matches against the editor's word, which is letters, digits,
    /// '_' and '-': "Grid.Styles" by "Styles", "vm:Card" by "Card", "{Bind }" by "Bind".</summary>
    private static string FilterText(string label)
    {
        int cut = label.LastIndexOfAny(['.', ':']);
        string tail = cut >= 0 && cut < label.Length - 1 ? label.Substring(cut + 1) : label;

        return tail.Trim('{', '}', '@', '$', '"', ' ');
    }

    private static string Description(JToken entry)
    {
        string? detail = (string?)entry["detail"];
        string? documentation = (string?)entry["documentation"]?["value"];

        return detail is null ? documentation ?? string.Empty
            : documentation is null ? detail
            : detail + Environment.NewLine + Environment.NewLine + documentation;
    }

    public Task<object> GetDescriptionAsync(IAsyncCompletionSession session, CompletionItem item, CancellationToken token) =>
        Task.FromResult<object>(item.Properties.TryGetProperty(DescriptionKey, out string description) ? description : string.Empty);
}

/// <summary>Commits an item of the server's: its text over the word, the caret where the
/// server put $1 — between the quotes of <c>Text=""</c>.</summary>
[Export(typeof(IAsyncCompletionCommitManagerProvider))]
[Name("ZeppelinForms markup and style sheet commit")]
[ContentType("text")]
internal sealed class ZfCommitManagerProvider : IAsyncCompletionCommitManagerProvider
{
    public IAsyncCompletionCommitManager GetOrCreate(ITextView textView) =>
        textView.Properties.GetOrCreateSingletonProperty(() => new ZfCommitManager(textView));
}

internal sealed class ZfCommitManager(ITextView view) : IAsyncCompletionCommitManager
{
    public IEnumerable<char> PotentialCommitCharacters => [];

    public bool ShouldCommitCompletion(IAsyncCompletionSession session, SnapshotPoint location, char typedChar, CancellationToken token) => false;

    public CommitResult TryCommit(IAsyncCompletionSession session, ITextBuffer buffer, CompletionItem item, char typedChar, CancellationToken token)
    {
        if (!item.Properties.TryGetProperty(ZfCompletionSource.InsertKey, out string insert) ||
            !item.Properties.TryGetProperty(ZfCompletionSource.CaretKey, out int caret))
        {
            return CommitResult.Unhandled;
        }

        SnapshotSpan span = session.ApplicableToSpan.GetSpan(buffer.CurrentSnapshot);

        using (ITextEdit edit = buffer.CreateEdit())
        {
            edit.Replace(span, insert);
            edit.Apply();
        }

        view.Caret.MoveTo(new SnapshotPoint(buffer.CurrentSnapshot, span.Start.Position + caret));

        return CommitResult.Handled;
    }
}
