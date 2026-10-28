using System.Text.Json.Nodes;

namespace ZeppelinForms.LanguageServer.Protocol;

/// <summary>Offsets in a text and the protocol's positions: a zero-based line and a column
/// in UTF-16 code units — which is what a .NET string counts in.</summary>
internal static class TextPosition
{
    public static int OffsetOf(string text, int line, int character)
    {
        int offset = 0;

        for (int current = 0; current < line; current++)
        {
            int newline = text.IndexOf('\n', offset);
            if (newline < 0) return text.Length;
            offset = newline + 1;
        }

        int end = text.IndexOf('\n', offset);
        if (end < 0) end = text.Length;

        return Math.Min(offset + character, end);
    }

    public static (int Line, int Character) PositionOf(string text, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);

        int line = 0;
        int lineStart = 0;

        for (int i = 0; i < offset; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                lineStart = i + 1;
            }
        }

        return (line, offset - lineStart);
    }

    public static JsonObject Position(int line, int character) => new() { ["line"] = line, ["character"] = character };

    public static JsonObject Range(string text, int start, int end)
    {
        var (startLine, startCharacter) = PositionOf(text, start);
        var (endLine, endCharacter) = PositionOf(text, end);

        return new JsonObject
        {
            ["start"] = Position(startLine, startCharacter),
            ["end"] = Position(endLine, endCharacter),
        };
    }

    public static JsonObject Range(int startLine, int startCharacter, int endLine, int endCharacter) => new()
    {
        ["start"] = Position(startLine, startCharacter),
        ["end"] = Position(endLine, endCharacter),
    };
}
