using System.Text.Json.Nodes;
using ZeppelinForms.LanguageServer.Markup;
using ZeppelinForms.LanguageServer.Protocol;
using ZeppelinForms.LanguageServer.Styles;
using ZeppelinForms.LanguageServer.Workspace;

namespace ZeppelinForms.LanguageServer;

/// <summary>
/// The language server: the protocol's requests and notifications to the markup's and the
/// sheets' completion, hover and problems.
/// </summary>
/// <remarks>
/// <para>
/// Requests are answered one at a time, in order, on the reading loop. Problems are
/// computed in the background a moment after the last change — typing doesn't wait for
/// them, and a burst of keys computes them once.
/// </para>
/// <para>
/// The text is synchronized whole (TextDocumentSyncKind.Full): markup and sheets are small,
/// and a whole text can't drift from the editor's.
/// </para>
/// </remarks>
internal sealed class Server(JsonRpc rpc, TextWriter log)
{
    private const int MethodNotFound = -32601;
    private const int InternalError = -32603;

    /// <summary>How long after the last change the problems are computed.</summary>
    private static readonly TimeSpan ProblemsDelay = TimeSpan.FromMilliseconds(400);

    private readonly Documents _documents = new();
    private readonly Dictionary<string, CancellationTokenSource> _pendingProblems = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _problemsSync = new();

    private bool _shutdown;

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        while (await rpc.ReadAsync(cancellationToken) is { } message)
        {
            string? method = (string?)message["method"];
            JsonNode? id = message["id"];
            JsonNode? parameters = message["params"];

            if (method is null) continue;   // a response to a request of ours: none are made

            if (method == "exit") return _shutdown ? 0 : 1;

            try
            {
                JsonNode? result = Handle(method, parameters, out bool known);

                if (id is null) continue;   // a notification wants no answer

                if (known) rpc.Respond(id, result);
                else rpc.RespondError(id, MethodNotFound, $"'{method}' is not supported");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                log.WriteLine($"[zf-ls] {method} failed: {exception}");

                if (id is not null) rpc.RespondError(id, InternalError, exception.Message);
            }
        }

        return _shutdown ? 0 : 1;
    }

    private JsonNode? Handle(string method, JsonNode? parameters, out bool known)
    {
        known = true;

        switch (method)
        {
            case "initialize":
                return Initialize();

            case "initialized":
            case "$/cancelRequest":
            case "$/setTrace":
            case "workspace/didChangeConfiguration":
            case "workspace/didChangeWatchedFiles":
                return null;

            case "shutdown":
                _shutdown = true;
                return null;

            case "textDocument/didOpen":
                {
                    string path = PathOf(parameters!["textDocument"]!);
                    _documents.Set(path, (string?)parameters["textDocument"]!["text"] ?? string.Empty);
                    ScheduleProblems(path);
                    return null;
                }

            case "textDocument/didChange":
                {
                    string path = PathOf(parameters!["textDocument"]!);

                    // whole texts: the last change is the document
                    if (parameters["contentChanges"] is JsonArray { Count: > 0 } changes)
                        _documents.Set(path, (string?)changes[^1]!["text"] ?? string.Empty);

                    ScheduleProblems(path);
                    return null;
                }

            case "textDocument/didSave":
                ScheduleProblems(PathOf(parameters!["textDocument"]!));
                return null;

            case "textDocument/didClose":
                {
                    string path = PathOf(parameters!["textDocument"]!);
                    _documents.Close(path);
                    CancelProblems(path);

                    // a closed document shows no problems: the build's are in the Error List
                    rpc.Notify("textDocument/publishDiagnostics", new JsonObject
                    {
                        ["uri"] = Documents.UriOf(path),
                        ["diagnostics"] = new JsonArray(),
                    });

                    return null;
                }

            case "textDocument/completion":
                return Completion(parameters!);

            case "textDocument/hover":
                return Hover(parameters!);

            default:
                known = false;
                return null;
        }
    }

    private static JsonObject Initialize() => new()
    {
        ["capabilities"] = new JsonObject
        {
            ["textDocumentSync"] = new JsonObject
            {
                ["openClose"] = true,
                ["change"] = 1,   // Full
                ["save"] = new JsonObject { ["includeText"] = false },
            },
            ["completionProvider"] = new JsonObject
            {
                ["triggerCharacters"] = new JsonArray("<", ":", " ", ".", "\"", "{", "@", "$", ","),
                ["resolveProvider"] = false,
            },
            ["hoverProvider"] = true,
        },
        ["serverInfo"] = new JsonObject
        {
            ["name"] = "ZeppelinForms",
            ["version"] = typeof(Server).Assembly.GetName().Version?.ToString(3),
        },
    };

    // ===== completion and hover =====

    private JsonNode? Completion(JsonNode parameters)
    {
        string path = PathOf(parameters["textDocument"]!);

        if (_documents.Read(path) is not { } text || ProjectContext.For(path) is not { } project) return null;

        int position = TextPosition.OffsetOf(text, (int)parameters["position"]!["line"]!, (int)parameters["position"]!["character"]!);

        var symbols = new MarkupSymbols(project.Snapshot(_documents, CancellationToken.None));
        if (symbols.Element is null) return null;

        (IReadOnlyList<CompletionEntry> items, int from) = path.EndsWith(".zss", StringComparison.OrdinalIgnoreCase)
            ? StyleCompletion.Complete(text, position, symbols)
            : MarkupCompletion.Complete(text, position, symbols);

        JsonObject range = TextPosition.Range(text, from, position);
        var list = new JsonArray();

        foreach (CompletionEntry entry in items.GroupBy(i => i.Label).Select(g => g.First()))
        {
            // "Text=\"$1\"$0": the caret goes between the quotes
            bool snippet = entry.Insert is { } insert && (insert.Contains("$1") || insert.Contains("$0"));

            var item = new JsonObject
            {
                ["label"] = entry.Label,
                ["kind"] = (int)entry.Kind,
                ["sortText"] = $"{entry.Rank}_{entry.Label}",
                ["filterText"] = entry.Label,
                ["textEdit"] = new JsonObject
                {
                    ["range"] = range.DeepClone(),
                    ["newText"] = entry.Insert ?? entry.Label,
                },
            };

            if (snippet) item["insertTextFormat"] = 2;
            if (entry.Detail is not null) item["detail"] = entry.Detail;
            if (entry.Documentation is not null) item["documentation"] = new JsonObject { ["kind"] = "markdown", ["value"] = entry.Documentation };

            list.Add(item);
        }

        return new JsonObject { ["isIncomplete"] = false, ["items"] = list };
    }

    private JsonNode? Hover(JsonNode parameters)
    {
        string path = PathOf(parameters["textDocument"]!);

        if (!path.EndsWith(".zfml", StringComparison.OrdinalIgnoreCase)) return null;
        if (_documents.Read(path) is not { } text || ProjectContext.For(path) is not { } project) return null;

        int position = TextPosition.OffsetOf(text, (int)parameters["position"]!["line"]!, (int)parameters["position"]!["character"]!);

        var symbols = new MarkupSymbols(project.Snapshot(_documents, CancellationToken.None));
        if (symbols.Element is null) return null;

        if (MarkupHover.At(text, position, symbols) is not { } hover) return null;

        return new JsonObject
        {
            ["contents"] = new JsonObject { ["kind"] = "markdown", ["value"] = hover.Markdown },
            ["range"] = TextPosition.Range(text, hover.Start, hover.End),
        };
    }

    // ===== problems =====

    private void ScheduleProblems(string path)
    {
        if (!path.EndsWith(".zfml", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".zss", StringComparison.OrdinalIgnoreCase))
            return;

        CancellationTokenSource cancel;

        lock (_problemsSync)
        {
            if (_pendingProblems.Remove(path, out CancellationTokenSource? previous)) previous.Cancel();
            _pendingProblems[path] = cancel = new CancellationTokenSource();
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(ProblemsDelay, cancel.Token);

                if (_documents.Read(path) is not { } text || ProjectContext.For(path) is not { } project) return;

                ProjectSnapshot snapshot = project.Snapshot(_documents, cancel.Token);
                JsonArray problems = Problems.Of(path, text, snapshot);

                cancel.Token.ThrowIfCancellationRequested();

                rpc.Notify("textDocument/publishDiagnostics", new JsonObject
                {
                    ["uri"] = Documents.UriOf(path),
                    ["diagnostics"] = problems,
                });
            }
            catch (OperationCanceledException)
            {
                // a newer change computes them
            }
            catch (Exception exception)
            {
                log.WriteLine($"[zf-ls] problems of {path} failed: {exception}");
            }
        });
    }

    private void CancelProblems(string path)
    {
        lock (_problemsSync)
            if (_pendingProblems.Remove(path, out CancellationTokenSource? pending)) pending.Cancel();
    }

    private static string PathOf(JsonNode textDocument) => Documents.PathOf((string)textDocument["uri"]!);
}
