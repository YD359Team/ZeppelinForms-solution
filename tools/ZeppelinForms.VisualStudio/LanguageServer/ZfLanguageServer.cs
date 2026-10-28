using System.Diagnostics;
using System.IO;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

namespace ZeppelinForms.VisualStudio.LanguageServer;

/// <summary>
/// The ZeppelinForms language server, shipped in the extension's LanguageServer folder and
/// run by the machine's .NET: completion and hover for .zfml and .zss, the same server Rider
/// and VS Code use. One process for Visual Studio, started on the first request.
/// </summary>
/// <remarks>
/// <para>
/// Visual Studio's own LSP client wants a content type of its own for the files, and .zfml
/// opens in the XML editor — kept for its tags, folding and formatting. So the editor's
/// completion and quick info sources ask the server themselves, over JSON-RPC on the
/// process's stdin and stdout, with the document's whole text before each request.
/// </para>
/// <para>
/// Problems are not taken from it: the markup generator puts them in the Error List as the
/// project is edited, and the style sheet checker does for .zss.
/// </para>
/// </remarks>
internal sealed class ZfLanguageServer : IDisposable
{
    private static readonly object StartSync = new();
    private static ZfLanguageServer? s_instance;

    private readonly Process _process;
    private readonly JsonRpc _rpc;
    private readonly Task _initialized;

    /// <summary>What the server has of each document: the version and the text last sent.</summary>
    private readonly Dictionary<string, (int Version, string Text)> _sent = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _sync = new(1, 1);

    private ZfLanguageServer(Process process)
    {
        _process = process;

        _rpc = new JsonRpc(new HeaderDelimitedMessageHandler(process.StandardInput.BaseStream, process.StandardOutput.BaseStream, new JsonMessageFormatter()));
        _rpc.StartListening();

        _initialized = InitializeAsync();
    }

    /// <summary>The server in the extension's LanguageServer folder.</summary>
    public static string ServerPath =>
        Path.Combine(Path.GetDirectoryName(typeof(ZfLanguageServer).Assembly.Location)!, "LanguageServer", "ZeppelinForms.LanguageServer.dll");

    /// <summary>The running server, started — again, if it exited — on the first call.</summary>
    public static ZfLanguageServer? Instance
    {
        get
        {
            lock (StartSync)
            {
                if (s_instance is { _process.HasExited: false }) return s_instance;

                s_instance?.Dispose();
                s_instance = Start();

                return s_instance;
            }
        }
    }

    private static ZfLanguageServer? Start()
    {
        if (!File.Exists(ServerPath))
        {
            Log("The language server is missing from the extension: " + ServerPath);
            return null;
        }

        var start = new ProcessStartInfo("dotnet", $"\"{ServerPath}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(ServerPath),
        };

        try
        {
            Process process = Process.Start(start) ?? throw new InvalidOperationException("dotnet did not start.");

            // its log: read, or a full pipe would stop it
            process.ErrorDataReceived += (_, e) => { if (e.Data is { Length: > 0 } line) Log(line); };
            process.BeginErrorReadLine();

            return new ZfLanguageServer(process);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log("The language server needs the .NET 10 runtime, and 'dotnet' did not start: " + exception.Message);
            return null;
        }
    }

    private async Task InitializeAsync()
    {
        await _rpc.InvokeWithParameterObjectAsync<JToken>("initialize", new
        {
            processId = Process.GetCurrentProcess().Id,
            rootUri = (string?)null,
            capabilities = new { },
            clientInfo = new { name = "Visual Studio" },
        });

        await _rpc.NotifyWithParameterObjectAsync("initialized", new { });
    }

    /// <summary>The completion list at a position of a document; null — none.</summary>
    public Task<JToken?> CompletionAsync(string path, string text, int line, int character, CancellationToken cancellationToken) =>
        RequestAsync("textDocument/completion", path, text, line, character, cancellationToken);

    /// <summary>The hover at a position of a document; null — none.</summary>
    public Task<JToken?> HoverAsync(string path, string text, int line, int character, CancellationToken cancellationToken) =>
        RequestAsync("textDocument/hover", path, text, line, character, cancellationToken);

    private async Task<JToken?> RequestAsync(string method, string path, string text, int line, int character, CancellationToken cancellationToken)
    {
        await _initialized;

        string uri = new Uri(path).AbsoluteUri;

        // the text first: the server answers about what is in the editor, not on the disk
        await _sync.WaitAsync(cancellationToken);

        try
        {
            if (!_sent.TryGetValue(path, out var sent))
            {
                await _rpc.NotifyWithParameterObjectAsync("textDocument/didOpen", new
                {
                    textDocument = new { uri, languageId = path.EndsWith(".zss", StringComparison.OrdinalIgnoreCase) ? "zss" : "zfml", version = 1, text },
                });

                _sent[path] = (1, text);
            }
            else if (!string.Equals(sent.Text, text, StringComparison.Ordinal))
            {
                int version = sent.Version + 1;

                await _rpc.NotifyWithParameterObjectAsync("textDocument/didChange", new
                {
                    textDocument = new { uri, version },
                    contentChanges = new[] { new { text } },
                });

                _sent[path] = (version, text);
            }
        }
        finally
        {
            _sync.Release();
        }

        return await _rpc.InvokeWithParameterObjectAsync<JToken?>(method, new
        {
            textDocument = new { uri },
            position = new { line, character },
        }, cancellationToken);
    }

    private static void Log(string line) => ZeppelinFormsPackage.Instance?.Log?.Write("language server: " + line);

    public void Dispose()
    {
        _rpc.Dispose();

        try
        {
            if (!_process.HasExited) _process.Kill();
        }
        catch (InvalidOperationException)
        {
            // gone already
        }

        _process.Dispose();
    }

    /// <summary>The file of a ZeppelinForms document — .zfml or .zss — a buffer shows; null
    /// for any other.</summary>
    public static string? FileOf(Microsoft.VisualStudio.Text.ITextBuffer buffer)
    {
        if (!buffer.Properties.TryGetProperty(typeof(Microsoft.VisualStudio.Text.ITextDocument), out Microsoft.VisualStudio.Text.ITextDocument document))
            return null;

        string path = document.FilePath;

        return path.EndsWith(".zfml", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".zss", StringComparison.OrdinalIgnoreCase)
            ? path
            : null;
    }
}
