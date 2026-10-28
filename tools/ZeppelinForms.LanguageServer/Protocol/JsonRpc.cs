using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ZeppelinForms.LanguageServer.Protocol;

/// <summary>
/// JSON-RPC 2.0 the way the Language Server Protocol frames it: a <c>Content-Length</c>
/// header, an empty line, the UTF-8 JSON of one message.
/// </summary>
/// <remarks>
/// Reading is one loop on one thread; writing is guarded, since diagnostics computed in the
/// background answer between the replies to requests.
/// </remarks>
internal sealed class JsonRpc(Stream input, Stream output)
{
    private readonly Lock _writing = new();

    /// <summary>The next message; null — the client closed the stream.</summary>
    public async Task<JsonObject?> ReadAsync(CancellationToken cancellationToken)
    {
        int length = -1;

        while (true)
        {
            string? line = await ReadLineAsync(cancellationToken);
            if (line is null) return null;

            // the empty line ends the headers
            if (line.Length == 0)
            {
                if (length >= 0) break;
                continue;
            }

            int colon = line.IndexOf(':');
            if (colon > 0 && line[..colon].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                length = int.Parse(line[(colon + 1)..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
        }

        byte[] body = new byte[length];
        int read = 0;

        while (read < length)
        {
            int got = await input.ReadAsync(body.AsMemory(read, length - read), cancellationToken);
            if (got == 0) return null;
            read += got;
        }

        return JsonNode.Parse(body) as JsonObject;
    }

    /// <summary>A header line, without its CR LF; null at the end of the stream. Byte by
    /// byte: the body after the headers is not to be read ahead into a buffer.</summary>
    private async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        var line = new StringBuilder();
        byte[] one = new byte[1];

        while (true)
        {
            int got = await input.ReadAsync(one, cancellationToken);
            if (got == 0) return line.Length > 0 ? line.ToString() : null;

            if (one[0] == '\n') return line.ToString().TrimEnd('\r');

            line.Append((char)one[0]);
        }
    }

    public void Respond(JsonNode? id, JsonNode? result) =>
        Write(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["result"] = result });

    public void RespondError(JsonNode? id, int code, string message) =>
        Write(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
        });

    public void Notify(string method, JsonNode? parameters) =>
        Write(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = parameters });

    private void Write(JsonObject message)
    {
        byte[] body = Encoding.UTF8.GetBytes(message.ToJsonString(Options));
        byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");

        lock (_writing)
        {
            output.Write(header);
            output.Write(body);
            output.Flush();
        }
    }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };
}
