using System.Globalization;
using System.Text;

namespace ZeppelinForms.Core.Markdown;

/// <summary>
/// Inlines of one block: code spans, emphasis, links, images, autolinks, escapes,
/// entities and line breaks.
/// </summary>
/// <remarks>
/// Emphasis follows the delimiter-run rules of CommonMark: <c>*</c> and <c>_</c>
/// open where they lean against the text after them and close where they lean
/// against the text before, <c>_</c> not inside a word — so snake_case_names stay
/// as written — and runs pair by the rule of three. Brackets become links only with
/// a destination after them or a definition for their label; otherwise they are
/// text.
/// </remarks>
internal static class MarkdownInlineParser
{
    private abstract class Node;

    private sealed class TextNode(string text) : Node
    {
        public string Text = text;
    }

    /// <summary>A run of <c>*</c>, <c>_</c> or <c>~</c> waiting to be paired.</summary>
    private sealed class DelimiterNode(char symbol, int count, bool canOpen, bool canClose) : Node
    {
        public readonly char Symbol = symbol;
        public int Count = count;
        public readonly int OriginalCount = count;
        public readonly bool CanOpen = canOpen;
        public readonly bool CanClose = canClose;
    }

    /// <summary>A <c>[</c> or <c>![</c> waiting for its <c>]</c>.</summary>
    private sealed class BracketNode(bool isImage) : Node
    {
        public readonly bool IsImage = isImage;
        public bool Active = true;
    }

    private sealed class DoneNode(MarkdownInline inline) : Node
    {
        public readonly MarkdownInline Inline = inline;
    }

    private enum ContainerKind : byte { Emphasis, Strong, Strikethrough, Link, Image }

    private sealed class ContainerNode(ContainerKind kind, LinkedList<Node> children, string? url = null, string? title = null) : Node
    {
        public readonly ContainerKind Kind = kind;
        public readonly LinkedList<Node> Children = children;
        public readonly string? Url = url;
        public readonly string? Title = title;
    }

    public static IReadOnlyList<MarkdownInline> Parse(string text, MarkdownDocument? document)
    {
        var nodes = new LinkedList<Node>();
        var builder = new StringBuilder();

        void Flush()
        {
            if (builder.Length == 0) return;

            nodes.AddLast(new TextNode(builder.ToString()));
            builder.Clear();
        }

        int i = 0;

        while (i < text.Length)
        {
            char c = text[i];

            switch (c)
            {
                case '\\':
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        Flush();
                        nodes.AddLast(new DoneNode(new LineBreakInline()));
                        i += 2;
                        SkipLeadingSpaces(text, ref i);
                    }
                    else if (i + 1 < text.Length && IsAsciiPunctuation(text[i + 1]))
                    {
                        builder.Append(text[i + 1]);
                        i += 2;
                    }
                    else
                    {
                        builder.Append('\\');
                        i++;
                    }

                    continue;

                case '`':
                    {
                        int run = RunLength(text, i, '`');
                        int close = FindBacktickRun(text, i + run, run);

                        if (close < 0)
                        {
                            // an unclosed run is text, all of it
                            builder.Append('`', run);
                            i += run;
                            continue;
                        }

                        Flush();
                        nodes.AddLast(new DoneNode(new CodeInline(CodeContent(text[(i + run)..close]))));
                        i = close + run;
                        continue;
                    }

                case '*' or '_' or '~':
                    {
                        int run = RunLength(text, i, c);

                        // GitHub strikethrough is two tildes; a single one is text
                        if (c == '~' && run != 2)
                        {
                            builder.Append(c, run);
                            i += run;
                            continue;
                        }

                        char before = i > 0 ? text[i - 1] : '\n';
                        char after = i + run < text.Length ? text[i + run] : '\n';

                        bool leftFlanking = !char.IsWhiteSpace(after)
                            && (!IsPunctuation(after) || char.IsWhiteSpace(before) || IsPunctuation(before));
                        bool rightFlanking = !char.IsWhiteSpace(before)
                            && (!IsPunctuation(before) || char.IsWhiteSpace(after) || IsPunctuation(after));

                        bool canOpen, canClose;

                        if (c == '_')
                        {
                            // not inside a word: snake_case stays as written
                            canOpen = leftFlanking && (!rightFlanking || IsPunctuation(before));
                            canClose = rightFlanking && (!leftFlanking || IsPunctuation(after));
                        }
                        else
                        {
                            canOpen = leftFlanking;
                            canClose = rightFlanking;
                        }

                        Flush();
                        nodes.AddLast(new DelimiterNode(c, run, canOpen, canClose));
                        i += run;
                        continue;
                    }

                case '!' when i + 1 < text.Length && text[i + 1] == '[':
                    Flush();
                    nodes.AddLast(new BracketNode(isImage: true));
                    i += 2;
                    continue;

                case '[':
                    Flush();
                    nodes.AddLast(new BracketNode(isImage: false));
                    i++;
                    continue;

                case ']':
                    Flush();
                    i = CloseBracket(text, i, nodes, document);
                    continue;

                case '<':
                    if (TryAutolink(text, i, out int autolinkEnd, out string url, out string label))
                    {
                        Flush();
                        nodes.AddLast(new DoneNode(new LinkInline([new TextInline(label)], url, null)));
                        i = autolinkEnd;
                        continue;
                    }

                    builder.Append(c);
                    i++;
                    continue;

                case '&':
                    if (TryEntity(text, i, out int entityEnd, out string decoded))
                    {
                        builder.Append(decoded);
                        i = entityEnd;
                        continue;
                    }

                    builder.Append(c);
                    i++;
                    continue;

                case '\n':
                    {
                        // two spaces before the end of a line break it; otherwise the
                        // line goes on as a space
                        int spaces = 0;
                        while (builder.Length - spaces > 0 && builder[builder.Length - 1 - spaces] == ' ') spaces++;

                        builder.Length -= spaces;
                        i++;
                        SkipLeadingSpaces(text, ref i);

                        if (spaces >= 2)
                        {
                            Flush();
                            nodes.AddLast(new DoneNode(new LineBreakInline()));
                        }
                        else
                        {
                            builder.Append(' ');
                        }

                        continue;
                    }

                default:
                    if (TryBareUrl(text, i, out int urlEnd, out string bare))
                    {
                        Flush();

                        string target = bare.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "http://" + bare : bare;
                        nodes.AddLast(new DoneNode(new LinkInline([new TextInline(bare)], target, null)));

                        i = urlEnd;
                        continue;
                    }

                    builder.Append(c);
                    i++;
                    continue;
            }
        }

        Flush();

        ProcessEmphasis(nodes, bottom: null);

        return Convert(nodes);
    }

    // ===== brackets =====

    /// <summary>A <c>]</c>: the nearest active bracket becomes a link or an image
    /// if a destination or a known label follows; otherwise both are text.</summary>
    private static int CloseBracket(string text, int i, LinkedList<Node> nodes, MarkdownDocument? document)
    {
        LinkedListNode<Node>? opener = nodes.Last;

        while (opener is not null && opener.Value is not BracketNode) opener = opener.Previous;

        if (opener is null)
        {
            nodes.AddLast(new TextNode("]"));
            return i + 1;
        }

        var bracket = (BracketNode)opener.Value;

        if (!bracket.Active)
        {
            opener.Value = new TextNode(bracket.IsImage ? "![" : "[");
            nodes.AddLast(new TextNode("]"));
            return i + 1;
        }

        int after = i + 1;
        string? url = null;
        string? title = null;

        if (after < text.Length && text[after] == '(' && TryInlineDestination(text, after, out int end, out string destination, out string? inlineTitle))
        {
            url = destination;
            title = inlineTitle;
            after = end;
        }
        else if (document is not null)
        {
            // [text][label], [label][] and the bare [label]
            string label = TextBetween(opener, nodes);

            if (after + 1 < text.Length && text[after] == '[')
            {
                int close = text.IndexOf(']', after + 1);

                if (close > after)
                {
                    string explicitLabel = text[(after + 1)..close];
                    string key = explicitLabel.Length > 0 ? explicitLabel : label;

                    if (document.TryResolveReference(key, out string found, out string? foundTitle))
                    {
                        url = found;
                        title = foundTitle;
                        after = close + 1;
                    }
                }
            }
            else if (document.TryResolveReference(label, out string found, out string? foundTitle))
            {
                url = found;
                title = foundTitle;
            }
        }

        if (url is null)
        {
            opener.Value = new TextNode(bracket.IsImage ? "![" : "[");
            nodes.AddLast(new TextNode("]"));
            return i + 1;
        }

        // what is between the brackets is the link's text, its own emphasis paired first
        ProcessEmphasis(nodes, bottom: opener);

        var children = new LinkedList<Node>();

        while (opener.Next is { } next)
        {
            nodes.Remove(next);
            children.AddLast(next.Value);
        }

        opener.Value = new ContainerNode(bracket.IsImage ? ContainerKind.Image : ContainerKind.Link, children, url, title);

        // no links inside links: the brackets before this one stay text
        if (!bracket.IsImage)
            for (LinkedListNode<Node>? earlier = opener.Previous; earlier is not null; earlier = earlier.Previous)
                if (earlier.Value is BracketNode { IsImage: false } outer)
                    outer.Active = false;

        return after;
    }

    private static string TextBetween(LinkedListNode<Node> opener, LinkedList<Node> nodes)
    {
        var builder = new StringBuilder();

        for (LinkedListNode<Node>? node = opener.Next; node is not null; node = node.Next)
        {
            switch (node.Value)
            {
                case TextNode t: builder.Append(t.Text); break;
                case DelimiterNode d: builder.Append(d.Symbol, d.Count); break;
                case DoneNode { Inline: CodeInline code }: builder.Append('`').Append(code.Code).Append('`'); break;
            }
        }

        return builder.ToString();
    }

    /// <summary><c>(destination "title")</c> right after the <c>]</c>.</summary>
    private static bool TryInlineDestination(string text, int open, out int end, out string url, out string? title)
    {
        end = open;
        url = string.Empty;
        title = null;

        int i = open + 1;
        SkipWhitespace(text, ref i);

        var destination = new StringBuilder();

        if (i < text.Length && text[i] == '<')
        {
            i++;

            while (i < text.Length && text[i] != '>')
            {
                if (text[i] == '\n') return false;
                destination.Append(text[i]);
                i++;
            }

            if (i >= text.Length) return false;
            i++;
        }
        else
        {
            int depth = 0;

            while (i < text.Length && !char.IsWhiteSpace(text[i]))
            {
                char c = text[i];

                if (c == '\\' && i + 1 < text.Length && IsAsciiPunctuation(text[i + 1]))
                {
                    destination.Append(text[i + 1]);
                    i += 2;
                    continue;
                }

                if (c == '(') depth++;
                else if (c == ')')
                {
                    if (depth == 0) break;
                    depth--;
                }

                destination.Append(c);
                i++;
            }
        }

        SkipWhitespace(text, ref i);

        if (i < text.Length && text[i] is '"' or '\'' or '(')
        {
            char close = text[i] == '(' ? ')' : text[i];
            int start = i + 1;
            int stop = text.IndexOf(close, start);

            if (stop < 0) return false;

            title = text[start..stop];
            i = stop + 1;
            SkipWhitespace(text, ref i);
        }

        if (i >= text.Length || text[i] != ')') return false;

        url = destination.ToString();
        end = i + 1;
        return true;
    }

    // ===== emphasis =====

    /// <summary>Pair the delimiter runs after <paramref name="bottom"/>, the CommonMark way.</summary>
    private static void ProcessEmphasis(LinkedList<Node> nodes, LinkedListNode<Node>? bottom)
    {
        LinkedListNode<Node>? closer = bottom?.Next ?? (bottom is null ? nodes.First : null);

        while (closer is not null)
        {
            if (closer.Value is not DelimiterNode { CanClose: true } close)
            {
                closer = closer.Next;
                continue;
            }

            LinkedListNode<Node>? opener = closer.Previous;
            DelimiterNode? open = null;

            while (opener is not null && !ReferenceEquals(opener, bottom))
            {
                if (opener.Value is DelimiterNode candidate
                    && candidate.Symbol == close.Symbol
                    && candidate.CanOpen
                    && candidate.Count > 0
                    && !BreaksRuleOfThree(candidate, close)
                    && (close.Symbol != '~' || (candidate.Count >= 2 && close.Count >= 2)))
                {
                    open = candidate;
                    break;
                }

                opener = opener.Previous;
            }

            if (open is null || opener is null)
            {
                closer = closer.Next;
                continue;
            }

            int use = close.Symbol == '~' ? 2 : (open.Count >= 2 && close.Count >= 2 ? 2 : 1);

            ContainerKind kind = close.Symbol == '~' ? ContainerKind.Strikethrough
                : use == 2 ? ContainerKind.Strong
                : ContainerKind.Emphasis;

            var children = new LinkedList<Node>();

            while (!ReferenceEquals(opener.Next, closer))
            {
                LinkedListNode<Node> inner = opener.Next!;
                nodes.Remove(inner);
                children.AddLast(inner.Value);
            }

            nodes.AddAfter(opener, new ContainerNode(kind, children));

            open.Count -= use;
            close.Count -= use;

            if (open.Count == 0) nodes.Remove(opener);

            if (close.Count == 0)
            {
                LinkedListNode<Node>? next = closer.Next;
                nodes.Remove(closer);
                closer = next;
            }
        }
    }

    /// <summary>A run that can both open and close pairs only with one whose length
    /// doesn't sum with its own to a multiple of three — <c>*foo**bar**baz*</c>.</summary>
    private static bool BreaksRuleOfThree(DelimiterNode opener, DelimiterNode closer)
    {
        if (!(opener.CanClose || closer.CanOpen)) return false;

        int sum = opener.OriginalCount + closer.OriginalCount;

        return sum % 3 == 0 && !(opener.OriginalCount % 3 == 0 && closer.OriginalCount % 3 == 0);
    }

    // ===== output =====

    private static List<MarkdownInline> Convert(LinkedList<Node> nodes)
    {
        var result = new List<MarkdownInline>();
        var text = new StringBuilder();

        void FlushText()
        {
            if (text.Length == 0) return;

            result.Add(new TextInline(text.ToString()));
            text.Clear();
        }

        foreach (Node node in nodes)
        {
            switch (node)
            {
                case TextNode t:
                    text.Append(t.Text);
                    break;

                case DelimiterNode d:
                    // a run nobody paired is what it was written as
                    text.Append(d.Symbol, d.Count);
                    break;

                case BracketNode b:
                    text.Append(b.IsImage ? "![" : "[");
                    break;

                case DoneNode done:
                    FlushText();
                    result.Add(done.Inline);
                    break;

                case ContainerNode container:
                    FlushText();
                    List<MarkdownInline> children = Convert(container.Children);

                    result.Add(container.Kind switch
                    {
                        ContainerKind.Emphasis => new EmphasisInline(children),
                        ContainerKind.Strong => new StrongInline(children),
                        ContainerKind.Strikethrough => new StrikethroughInline(children),
                        ContainerKind.Link => new LinkInline(children, container.Url!, container.Title),
                        _ => new ImageInline(MarkdownInline.PlainText(children), container.Url!, container.Title),
                    });
                    break;
            }
        }

        FlushText();
        return result;
    }

    // ===== small pieces =====

    private static int RunLength(string text, int start, char c)
    {
        int i = start;
        while (i < text.Length && text[i] == c) i++;
        return i - start;
    }

    /// <summary>The start of a backtick run of exactly <paramref name="length"/>.</summary>
    private static int FindBacktickRun(string text, int from, int length)
    {
        int i = from;

        while (i < text.Length)
        {
            if (text[i] != '`')
            {
                i++;
                continue;
            }

            int run = RunLength(text, i, '`');

            if (run == length) return i;

            i += run;
        }

        return -1;
    }

    /// <summary>A code span's content: line ends become spaces, and one space on
    /// each side is dropped when both are there — <c>`` `tick` ``</c>.</summary>
    private static string CodeContent(string raw)
    {
        string content = raw.Replace('\n', ' ');

        if (content.Length >= 2 && content[0] == ' ' && content[^1] == ' ' && content.Trim().Length > 0)
            content = content[1..^1];

        return content;
    }

    private static bool TryAutolink(string text, int start, out int end, out string url, out string label)
    {
        end = start;
        url = label = string.Empty;

        int close = text.IndexOf('>', start + 1);
        if (close < 0) return false;

        string inside = text[(start + 1)..close];

        if (inside.Length == 0 || inside.Any(char.IsWhiteSpace) || inside.Contains('<')) return false;

        int colon = inside.IndexOf(':');

        if (colon >= 2 && char.IsAsciiLetter(inside[0]) && inside[..colon].All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '.' or '-'))
        {
            url = label = inside;
            end = close + 1;
            return true;
        }

        int at = inside.IndexOf('@');

        if (at > 0 && at < inside.Length - 1 && inside.IndexOf('.', at) > at)
        {
            label = inside;
            url = "mailto:" + inside;
            end = close + 1;
            return true;
        }

        return false;
    }

    /// <summary>A bare <c>https://…</c> or <c>www.…</c> at the start of a word, as
    /// GitHub links it; trailing punctuation stays outside.</summary>
    private static bool TryBareUrl(string text, int start, out int end, out string url)
    {
        end = start;
        url = string.Empty;

        if (start > 0 && !char.IsWhiteSpace(text[start - 1]) && text[start - 1] is not ('(' or '*' or '_' or '~'))
            return false;

        ReadOnlySpan<char> rest = text.AsSpan(start);

        bool scheme = rest.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || rest.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
        bool www = rest.StartsWith("www.", StringComparison.OrdinalIgnoreCase);

        if (!scheme && !www) return false;

        int i = start;
        while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '<') i++;

        // the sentence's own punctuation, and an unbalanced closing parenthesis
        while (i > start && text[i - 1] is '.' or ',' or ':' or ';' or '!' or '?' or '"' or '\'' or '*' or '_' or '~')
            i--;

        while (i > start && text[i - 1] == ')'
               && text.AsSpan(start, i - start).Count('(') < text.AsSpan(start, i - start).Count(')'))
            i--;

        string candidate = text[start..i];

        // "https://" alone is not a link
        if (candidate.Length <= (www ? 4 : candidate.IndexOf("//", StringComparison.Ordinal) + 2)) return false;

        url = candidate;
        end = i;
        return true;
    }

    private static readonly Dictionary<string, string> NamedEntities = new(StringComparer.Ordinal)
    {
        ["amp"] = "&",
        ["lt"] = "<",
        ["gt"] = ">",
        ["quot"] = "\"",
        ["apos"] = "'",
        ["nbsp"] = "\u00A0",
        ["copy"] = "©",
        ["reg"] = "®",
        ["trade"] = "™",
        ["mdash"] = "—",
        ["ndash"] = "–",
        ["hellip"] = "…",
        ["laquo"] = "«",
        ["raquo"] = "»",
        ["larr"] = "←",
        ["rarr"] = "→",
        ["times"] = "×",
        ["deg"] = "°",
    };

    private static bool TryEntity(string text, int start, out int end, out string decoded)
    {
        end = start;
        decoded = string.Empty;

        int semicolon = text.IndexOf(';', start + 1);

        if (semicolon < 0 || semicolon - start > 32) return false;

        string name = text[(start + 1)..semicolon];

        if (name.StartsWith('#'))
        {
            bool hex = name.Length > 1 && name[1] is 'x' or 'X';
            string digits = hex ? name[2..] : name[1..];

            if (digits.Length == 0
                || !int.TryParse(digits, hex ? NumberStyles.HexNumber : NumberStyles.None, CultureInfo.InvariantCulture, out int code)
                || code is <= 0 or > 0x10FFFF or (>= 0xD800 and <= 0xDFFF))
                return false;

            decoded = char.ConvertFromUtf32(code);
        }
        else if (!NamedEntities.TryGetValue(name, out decoded!))
        {
            return false;
        }

        end = semicolon + 1;
        return true;
    }

    private static void SkipLeadingSpaces(string text, ref int i)
    {
        while (i < text.Length && text[i] == ' ') i++;
    }

    private static void SkipWhitespace(string text, ref int i)
    {
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
    }

    private static bool IsAsciiPunctuation(char c) =>
        c is >= '!' and <= '/' or >= ':' and <= '@' or >= '[' and <= '`' or >= '{' and <= '~';

    private static bool IsPunctuation(char c) =>
        IsAsciiPunctuation(c) || char.IsPunctuation(c) || char.IsSymbol(c);
}