using System.Globalization;
using System.Text;

namespace ZeppelinForms.Core.Markdown;

/// <summary>
/// A Markdown document parsed into blocks and inlines: CommonMark with the GitHub
/// additions people write every day — tables, task lists, strikethrough, bare links.
/// </summary>
/// <remarks>
/// <para>
/// The parser is forgiving, as Markdown is: nothing is an error, what doesn't parse
/// as something else is text. It covers what README files and chat messages use,
/// not every corner of the specification: raw HTML is shown as text, and there are
/// no footnotes or math.
/// </para>
/// <para>
/// Headings get anchors the GitHub way, so that <c>[see](#getting-started)</c>
/// finds "Getting started"; repeated titles get <c>-1</c>, <c>-2</c>.
/// </para>
/// </remarks>
public sealed class MarkdownDocument
{
    private readonly Dictionary<string, (string Url, string? Title)> _references;
    private readonly Dictionary<string, int> _slugs = [];

    private MarkdownDocument(string source)
    {
        List<string> lines = SplitLines(source);

        // the definitions of [text][ref] links are lifted out first: a reference
        // may come before its definition, at the end of the file
        _references = TakeReferenceDefinitions(lines);

        Blocks = ParseBlocks(lines);
    }

    public static readonly MarkdownDocument Empty = new(string.Empty);

    public IReadOnlyList<MarkdownBlock> Blocks { get; }

    public static MarkdownDocument Parse(string? markdown) =>
        string.IsNullOrEmpty(markdown) ? Empty : new MarkdownDocument(markdown);

    /// <summary>A heading's anchor as GitHub makes it: lower case, punctuation
    /// dropped, spaces turned into hyphens.</summary>
    public static string Slug(string text)
    {
        var builder = new StringBuilder(text.Length);

        foreach (char c in text.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_') builder.Append(c);
            else if (c == ' ') builder.Append('-');
        }

        return builder.ToString();
    }

    // ===== lines =====

    private static List<string> SplitLines(string source)
    {
        string[] raw = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var lines = new List<string>(raw.Length);

        foreach (string line in raw)
            lines.Add(ExpandLeadingTabs(line));

        return lines;
    }

    /// <summary>A tab in the indentation counts to the next stop of four, as the
    /// specification has it; tabs inside a line stay.</summary>
    private static string ExpandLeadingTabs(string line)
    {
        if (!line.Contains('\t')) return line;

        var builder = new StringBuilder();
        int i = 0;

        for (; i < line.Length && line[i] is ' ' or '\t'; i++)
        {
            if (line[i] == ' ') builder.Append(' ');
            else builder.Append(' ', 4 - builder.Length % 4);
        }

        return builder.Append(line, i, line.Length - i).ToString();
    }

    private static bool IsBlank(string line) => string.IsNullOrWhiteSpace(line);

    private static int Indent(string line)
    {
        int i = 0;
        while (i < line.Length && line[i] == ' ') i++;
        return i;
    }

    /// <summary>Remove up to <paramref name="count"/> leading spaces.</summary>
    private static string Dedent(string line, int count)
    {
        int i = 0;
        while (i < count && i < line.Length && line[i] == ' ') i++;
        return line[i..];
    }

    // ===== reference definitions =====

    private static Dictionary<string, (string, string?)> TakeReferenceDefinitions(List<string> lines)
    {
        var references = new Dictionary<string, (string, string?)>(StringComparer.OrdinalIgnoreCase);
        bool inFence = false;
        string? fence = null;

        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            string trimmed = line.TrimStart();

            if (Indent(line) < 4 && TryFenceOpening(trimmed, out char ch, out int length, out _))
            {
                string marker = new(ch, length);

                if (!inFence) { inFence = true; fence = marker; continue; }
                if (trimmed.TrimEnd().All(c => c == ch) && trimmed.TrimEnd().Length >= fence!.Length && fence[0] == ch)
                {
                    inFence = false;
                    continue;
                }
            }

            if (inFence || Indent(line) >= 4) continue;

            if (TryReferenceDefinition(trimmed, out string label, out string url, out string? title))
            {
                references.TryAdd(NormalizeLabel(label), (url, title));
                lines.RemoveAt(i);
                i--;
            }
        }

        return references;
    }

    /// <summary><c>[label]: destination "title"</c> on a line of its own.</summary>
    private static bool TryReferenceDefinition(string line, out string label, out string url, out string? title)
    {
        label = url = string.Empty;
        title = null;

        if (!line.StartsWith('[')) return false;

        int close = line.IndexOf("]:", StringComparison.Ordinal);
        if (close <= 1) return false;

        label = line[1..close];
        if (label.Contains('[') || label.Contains(']')) return false;

        string rest = line[(close + 2)..].Trim();
        if (rest.Length == 0) return false;

        int end;

        if (rest[0] == '<')
        {
            end = rest.IndexOf('>');
            if (end < 0) return false;
            url = rest[1..end];
            end++;
        }
        else
        {
            end = 0;
            while (end < rest.Length && !char.IsWhiteSpace(rest[end])) end++;
            url = rest[..end];
        }

        string tail = rest[end..].Trim();

        if (tail.Length > 0)
        {
            char open = tail[0];
            char expected = open == '(' ? ')' : open;

            if (open is not ('"' or '\'' or '(') || tail.Length < 2 || tail[^1] != expected) return false;

            title = tail[1..^1];
        }

        return true;
    }

    internal static string NormalizeLabel(string label) =>
        string.Join(' ', label.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    internal bool TryResolveReference(string label, out string url, out string? title)
    {
        if (_references.TryGetValue(NormalizeLabel(label), out (string Url, string? Title) found))
        {
            (url, title) = found;
            return true;
        }

        url = string.Empty;
        title = null;
        return false;
    }

    // ===== blocks =====

    private List<MarkdownBlock> ParseBlocks(List<string> lines)
    {
        var blocks = new List<MarkdownBlock>();
        int i = 0;

        while (i < lines.Count)
        {
            string line = lines[i];

            if (IsBlank(line))
            {
                i++;
                continue;
            }

            int indent = Indent(line);

            if (indent >= 4)
            {
                blocks.Add(ParseIndentedCode(lines, ref i));
                continue;
            }

            string trimmed = line[indent..];

            if (TryFenceOpening(trimmed, out char fenceChar, out int fenceLength, out string info))
            {
                blocks.Add(ParseFencedCode(lines, ref i, indent, fenceChar, fenceLength, info));
                continue;
            }

            if (TryAtxHeading(trimmed, out int level, out string title))
            {
                blocks.Add(Heading(level, title));
                i++;
                continue;
            }

            if (IsRule(trimmed))
            {
                blocks.Add(new RuleBlock());
                i++;
                continue;
            }

            if (trimmed.StartsWith('>'))
            {
                blocks.Add(ParseQuote(lines, ref i));
                continue;
            }

            if (TryListMarker(line, out ListMarker marker))
            {
                blocks.Add(ParseList(lines, ref i, marker));
                continue;
            }

            if (i + 1 < lines.Count && TryTableStart(line, lines[i + 1], out List<TableAlignment> alignments))
            {
                blocks.Add(ParseTable(lines, ref i, alignments));
                continue;
            }

            blocks.Add(ParseParagraph(lines, ref i));
        }

        return blocks;
    }

    /// <summary>A line that starts a block of its own, and so ends a paragraph
    /// before it — except a numbered list that doesn't start at 1, which reads as
    /// a sentence beginning with a year more often than as a list.</summary>
    private static bool StartsBlock(string line)
    {
        if (IsBlank(line)) return true;

        int indent = Indent(line);
        if (indent >= 4) return false;

        string trimmed = line[indent..];

        return TryFenceOpening(trimmed, out _, out _, out _)
            || TryAtxHeading(trimmed, out _, out _)
            || IsRule(trimmed)
            || trimmed.StartsWith('>')
            || (TryListMarker(line, out ListMarker marker) && !marker.IsEmpty && (!marker.IsOrdered || marker.Number == 1));
    }

    // ----- code -----

    private static CodeBlock ParseIndentedCode(List<string> lines, ref int i)
    {
        var code = new List<string>();

        while (i < lines.Count && (IsBlank(lines[i]) || Indent(lines[i]) >= 4))
        {
            code.Add(Dedent(lines[i], 4));
            i++;
        }

        // blank lines after the code belong to whatever comes next
        while (code.Count > 0 && IsBlank(code[^1])) code.RemoveAt(code.Count - 1);

        return new CodeBlock(null, string.Join('\n', code));
    }

    private static bool TryFenceOpening(string trimmed, out char fenceChar, out int length, out string info)
    {
        fenceChar = '\0';
        length = 0;
        info = string.Empty;

        if (trimmed.Length < 3 || trimmed[0] is not ('`' or '~')) return false;

        fenceChar = trimmed[0];

        while (length < trimmed.Length && trimmed[length] == fenceChar) length++;

        if (length < 3) return false;

        info = trimmed[length..].Trim();

        // a backtick fence's info string has no backticks: ``` `code` ``` is inline code
        return !(fenceChar == '`' && info.Contains('`'));
    }

    private static CodeBlock ParseFencedCode(List<string> lines, ref int i, int indent, char fenceChar, int fenceLength, string info)
    {
        var code = new List<string>();
        i++;

        while (i < lines.Count)
        {
            string line = lines[i];
            string trimmed = line.TrimStart();

            // a closing fence: the same character, at least as long, nothing after it
            if (Indent(line) < 4 && trimmed.Length >= fenceLength && trimmed.TrimEnd().All(c => c == fenceChar))
            {
                i++;
                break;
            }

            code.Add(Dedent(line, indent));
            i++;
        }

        string language = info.Split(' ', 2)[0];

        return new CodeBlock(language.Length > 0 ? language : null, string.Join('\n', code));
    }

    // ----- headings and rules -----

    private static bool TryAtxHeading(string trimmed, out int level, out string text)
    {
        level = 0;
        text = string.Empty;

        while (level < trimmed.Length && trimmed[level] == '#') level++;

        if (level is < 1 or > 6) return false;
        if (level < trimmed.Length && trimmed[level] != ' ') return false;

        string rest = trimmed[level..].Trim();

        // an optional closing run of #, after a space: "## Title ##"
        int end = rest.Length;
        while (end > 0 && rest[end - 1] == '#') end--;

        if (end == 0) rest = string.Empty;
        else if (end < rest.Length && rest[end - 1] == ' ') rest = rest[..end].TrimEnd();

        text = rest;
        return true;
    }

    private HeadingBlock Heading(int level, string text)
    {
        IReadOnlyList<MarkdownInline> inlines = ParseInlines(text);
        string slug = Slug(MarkdownInline.PlainText(inlines));

        // repeated titles get a number, as GitHub numbers them
        if (_slugs.TryGetValue(slug, out int seen))
        {
            _slugs[slug] = seen + 1;
            slug = $"{slug}-{seen}";
        }
        else
        {
            _slugs[slug] = 1;
        }

        return new HeadingBlock(level, inlines, slug);
    }

    private static bool IsRule(string trimmed)
    {
        char mark = '\0';
        int count = 0;

        foreach (char c in trimmed)
        {
            if (c is ' ' or '\t') continue;

            if (c is not ('-' or '*' or '_')) return false;
            if (mark != '\0' && c != mark) return false;

            mark = c;
            count++;
        }

        return count >= 3;
    }

    // ----- quotes -----

    private QuoteBlock ParseQuote(List<string> lines, ref int i)
    {
        var inner = new List<string>();
        bool lastWasParagraph = false;

        while (i < lines.Count)
        {
            string line = lines[i];

            if (IsBlank(line)) break;

            string trimmed = line.TrimStart();

            if (Indent(line) < 4 && trimmed.StartsWith('>'))
            {
                string content = trimmed[1..];
                if (content.StartsWith(' ')) content = content[1..];

                inner.Add(content);
                lastWasParagraph = !IsBlank(content) && !StartsBlock(content);
            }
            else if (lastWasParagraph && !StartsBlock(line))
            {
                // a lazy continuation: the paragraph goes on without its marker
                inner.Add(trimmed);
            }
            else
            {
                break;
            }

            i++;
        }

        return new QuoteBlock(ParseBlocks(inner));
    }

    // ----- lists -----

    private readonly record struct ListMarker(
        bool IsOrdered, char Symbol, int Number, int Indent, int ContentIndent, string Rest, bool IsEmpty);

    private static bool TryListMarker(string line, out ListMarker marker)
    {
        marker = default;

        int indent = Indent(line);
        if (indent >= 4 || indent >= line.Length) return false;

        int p = indent;
        bool ordered;
        char symbol;
        int number = 0;

        if (line[p] is '-' or '+' or '*')
        {
            ordered = false;
            symbol = line[p];
            p++;
        }
        else
        {
            int digits = 0;
            while (p < line.Length && char.IsAsciiDigit(line[p]) && digits < 9) { p++; digits++; }

            if (digits == 0 || p >= line.Length || line[p] is not ('.' or ')')) return false;

            ordered = true;
            symbol = line[p];
            number = int.Parse(line.AsSpan(indent, digits), CultureInfo.InvariantCulture);
            p++;
        }

        // the marker is followed by a space or ends the line
        if (p < line.Length && line[p] != ' ') return false;

        int spaces = 0;
        while (p + spaces < line.Length && line[p + spaces] == ' ') spaces++;

        bool empty = p + spaces >= line.Length;

        // more than four spaces after the marker: the content is indented code,
        // and only one of them belongs to the marker
        if (empty || spaces > 4) spaces = 1;

        int contentIndent = p + spaces;
        string rest = empty ? string.Empty : line[Math.Min(line.Length, contentIndent)..];

        marker = new ListMarker(ordered, symbol, number, indent, contentIndent, rest, empty);
        return true;
    }

    private ListBlock ParseList(List<string> lines, ref int i, ListMarker first)
    {
        var items = new List<ListItemBlock>();
        bool loose = false;
        ListMarker marker = first;

        while (true)
        {
            var itemLines = new List<string> { marker.Rest };
            bool blankInside = false;
            bool lastBlank = marker.IsEmpty;
            i++;

            while (i < lines.Count)
            {
                string line = lines[i];

                if (IsBlank(line))
                {
                    // a blank line stays in the item only if the item goes on after it
                    int next = i + 1;
                    while (next < lines.Count && IsBlank(lines[next])) next++;

                    if (next < lines.Count && Indent(lines[next]) >= marker.ContentIndent)
                    {
                        itemLines.Add(string.Empty);
                        blankInside = true;
                        lastBlank = true;
                        i++;
                        continue;
                    }

                    break;
                }

                if (Indent(line) >= marker.ContentIndent)
                {
                    itemLines.Add(line[marker.ContentIndent..]);
                    lastBlank = false;
                    i++;
                    continue;
                }

                // a lazy continuation of the item's paragraph
                if (!lastBlank && !StartsBlock(line) && !TryListMarker(line, out _))
                {
                    itemLines.Add(line.TrimStart());
                    i++;
                    continue;
                }

                break;
            }

            bool? isChecked = TakeTaskMarker(itemLines);

            List<MarkdownBlock> blocks = ParseBlocks(itemLines);

            // blank lines between the item's own blocks make the list loose
            if (blankInside && blocks.Count > 1) loose = true;

            items.Add(new ListItemBlock(blocks, isChecked));

            // the next item: a marker of the same kind, not indented into this one
            int after = i;
            while (after < lines.Count && IsBlank(lines[after])) after++;

            if (after >= lines.Count
                || !TryListMarker(lines[after], out ListMarker following)
                || following.IsOrdered != first.IsOrdered
                || following.Symbol != first.Symbol
                || following.Indent >= marker.ContentIndent)
                break;

            // a rule written with the list's own bullet ends the list
            if (IsRule(lines[after].TrimStart())) break;

            if (after > i) loose = true;

            i = after;
            marker = following;
        }

        return new ListBlock(first.IsOrdered, first.Number, loose, items);
    }

    /// <summary><c>[ ]</c> or <c>[x]</c> at the start of an item: a task.</summary>
    private static bool? TakeTaskMarker(List<string> itemLines)
    {
        string head = itemLines[0];

        if (head.Length < 3 || head[0] != '[' || head[2] != ']') return null;
        if (head.Length > 3 && head[3] != ' ') return null;

        bool? state = head[1] switch
        {
            ' ' => false,
            'x' or 'X' => true,
            _ => null,
        };

        if (state is null) return null;

        itemLines[0] = head.Length > 4 ? head[4..] : string.Empty;
        return state;
    }

    // ----- tables -----

    private static bool TryTableStart(string header, string delimiter, out List<TableAlignment> alignments)
    {
        alignments = [];

        if (!header.Contains('|') || Indent(header) >= 4) return false;

        List<string> cells = SplitRow(delimiter);

        if (cells.Count == 0) return false;

        foreach (string raw in cells)
        {
            string cell = raw.Trim();

            if (cell.Length == 0) return false;

            bool left = cell.StartsWith(':');
            bool right = cell.EndsWith(':');
            string dashes = cell.Trim(':');

            if (dashes.Length == 0 || dashes.Any(c => c != '-')) return false;

            alignments.Add(left && right ? TableAlignment.Center
                : right ? TableAlignment.Right
                : left ? TableAlignment.Left
                : TableAlignment.None);
        }

        return SplitRow(header).Count == alignments.Count;
    }

    private TableBlock ParseTable(List<string> lines, ref int i, List<TableAlignment> alignments)
    {
        int columns = alignments.Count;

        IReadOnlyList<MarkdownInline>[] ParseRow(string line)
        {
            List<string> cells = SplitRow(line);
            var row = new IReadOnlyList<MarkdownInline>[columns];

            // a short row is padded, a long one cut: the header decides
            for (int c = 0; c < columns; c++)
                row[c] = c < cells.Count ? ParseInlines(cells[c].Trim()) : [];

            return row;
        }

        IReadOnlyList<MarkdownInline>[] header = ParseRow(lines[i]);
        i += 2;

        var rows = new List<IReadOnlyList<IReadOnlyList<MarkdownInline>>>();

        while (i < lines.Count && !IsBlank(lines[i]) && !(StartsBlock(lines[i]) && !lines[i].Contains('|')))
        {
            rows.Add(ParseRow(lines[i]));
            i++;
        }

        return new TableBlock(alignments, header, rows);
    }

    /// <summary>The cells of a row: split at pipes, but not at escaped ones or
    /// those inside a code span; the outer pipes are optional.</summary>
    private static List<string> SplitRow(string line)
    {
        string row = line.Trim();

        if (row.StartsWith('|')) row = row[1..];
        if (row.EndsWith('|') && !row.EndsWith("\\|")) row = row[..^1];

        var cells = new List<string>();
        var cell = new StringBuilder();
        bool inCode = false;

        for (int i = 0; i < row.Length; i++)
        {
            char c = row[i];

            if (c == '\\' && i + 1 < row.Length && row[i + 1] == '|')
            {
                cell.Append('|');
                i++;
                continue;
            }

            if (c == '`') inCode = !inCode;

            if (c == '|' && !inCode)
            {
                cells.Add(cell.ToString());
                cell.Clear();
                continue;
            }

            cell.Append(c);
        }

        cells.Add(cell.ToString());
        return cells;
    }

    // ----- paragraphs -----

    private MarkdownBlock ParseParagraph(List<string> lines, ref int i)
    {
        var text = new List<string> { lines[i].TrimStart() };
        i++;

        while (i < lines.Count)
        {
            string line = lines[i];
            string trimmed = line.TrimStart();

            // a line of = or - under the paragraph makes it a heading
            if (Indent(line) < 4 && trimmed.Length > 0)
            {
                string mark = trimmed.TrimEnd();

                if (mark.All(c => c == '=')) { i++; return Heading(1, string.Join('\n', text)); }
                if (mark.All(c => c == '-')) { i++; return Heading(2, string.Join('\n', text)); }
            }

            if (StartsBlock(line)) break;

            text.Add(trimmed);
            i++;
        }

        return new ParagraphBlock(ParseInlines(string.Join('\n', text)));
    }

    private IReadOnlyList<MarkdownInline> ParseInlines(string text) =>
        MarkdownInlineParser.Parse(text, this);
}