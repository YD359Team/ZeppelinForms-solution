namespace ZeppelinForms.Core.Text.Syntax;

/// <summary>Tokenizes source text with a trie of keywords and literal block delimiters.</summary>
/// <remarks>
/// Faster than a list of regexes for fixed lexemes: each character walks at most one
/// trie edge, and block openers are tried by length from longest to shortest.
/// The token list is rebuilt when the text changes; line-range invalidation can be
/// added later without changing the public API.
/// </remarks>
public sealed class SyntaxTokenizer
{
    private readonly SyntaxRuleSet _rules;
    private readonly KeywordTrie _trie;
    private readonly BlockRule[] _blocksByOpenLength;
    private readonly Func<char, bool> _isIdStart;
    private readonly Func<char, bool> _isIdPart;

    private string _text = string.Empty;
    private List<SyntaxToken> _tokens = [];

    public SyntaxTokenizer(SyntaxRuleSet rules)
    {
        _rules = rules;
        _trie = KeywordTrie.Build(rules.Keywords);
        _blocksByOpenLength = rules.Blocks
            .OrderByDescending(b => b.Open.Length)
            .ToArray();
        _isIdStart = rules.IsIdentifierStart ?? (c => char.IsLetter(c) || c == '_');
        _isIdPart = rules.IsIdentifierPart ?? (c => char.IsLetterOrDigit(c) || c == '_');
    }

    public SyntaxRuleSet Rules => _rules;

    public IReadOnlyList<SyntaxToken> Tokens => _tokens;

    public void Retokenize(string text)
    {
        _text = text ?? string.Empty;
        _tokens = Tokenize(_text);
    }

    public SyntaxStyle? StyleAt(int index)
    {
        // linear scan is fine for moderate files; binary search when tokens grow large
        foreach (SyntaxToken token in _tokens)
        {
            if (index >= token.Start && index < token.End)
                return token.Style;
        }

        return _rules.DefaultStyle;
    }

    private List<SyntaxToken> Tokenize(string text)
    {
        var tokens = new List<SyntaxToken>();
        int i = 0;
        int n = text.Length;

        while (i < n)
        {
            // block openers first — strings and comments win over keywords
            if (TryReadBlock(text, i, out int blockEnd, out SyntaxStyle? blockStyle) && blockStyle is not null)
            {
                tokens.Add(new SyntaxToken(i, blockEnd - i, blockStyle));
                i = blockEnd;
                continue;
            }

            char c = text[i];

            if (_isIdStart(c))
            {
                int start = i;
                i++;
                while (i < n && _isIdPart(text[i])) i++;

                string word = text[start..i];
                if (_trie.TryMatch(word, out SyntaxStyle? kwStyle) && kwStyle is not null)
                    tokens.Add(new SyntaxToken(start, i - start, kwStyle));
                continue;
            }

            if (char.IsDigit(c) && _rules.NumberStyle is { } numStyle)
            {
                int start = i;
                i++;
                while (i < n && (char.IsDigit(text[i]) || text[i] is '.' or 'x' or 'X' or 'b' or 'B' or '_'))
                    i++;
                tokens.Add(new SyntaxToken(start, i - start, numStyle));
                continue;
            }

            i++;
        }

        return tokens;
    }

    private bool TryReadBlock(string text, int index, out int end, out SyntaxStyle? style)
    {
        end = index;
        style = null;

        foreach (BlockRule block in _blocksByOpenLength)
        {
            if (!StartsWith(text, index, block.Open))
                continue;

            int pos = index + block.Open.Length;

            if (block.EndsAtLineBreak)
            {
                int lineEnd = text.IndexOf('\n', pos);
                end = lineEnd < 0 ? text.Length : lineEnd;
                style = block.Style;
                return true;
            }

            int depth = 1;
            while (pos < text.Length)
            {
                if (block.Escape is { } esc && text[pos] == esc && pos + 1 < text.Length)
                {
                    pos += 2;
                    continue;
                }

                if (block.AllowNesting && StartsWith(text, pos, block.Open))
                {
                    depth++;
                    pos += block.Open.Length;
                    continue;
                }

                if (StartsWith(text, pos, block.Close))
                {
                    depth--;
                    pos += block.Close.Length;
                    if (depth == 0)
                    {
                        end = pos;
                        style = block.Style;
                        return true;
                    }
                    continue;
                }

                pos++;
            }

            // unclosed: colour to the end
            end = text.Length;
            style = block.Style;
            return true;
        }

        return false;
    }

    private static bool StartsWith(string text, int index, string value)
    {
        if (index + value.Length > text.Length) return false;
        for (int i = 0; i < value.Length; i++)
        {
            if (text[index + i] != value[i]) return false;
        }
        return true;
    }

    /// <summary>Trie of keywords — O(length) lookup, no regex engine.</summary>
    private sealed class KeywordTrie
    {
        private readonly Node _root = new();

        public static KeywordTrie Build(IEnumerable<KeywordRule> rules)
        {
            var trie = new KeywordTrie();
            foreach (KeywordRule rule in rules)
                trie.Add(rule);
            return trie;
        }

        private void Add(KeywordRule rule)
        {
            string lex = rule.IgnoreCase ? rule.Lexeme.ToLowerInvariant() : rule.Lexeme;
            Node node = _root;
            foreach (char ch in lex)
            {
                char key = rule.IgnoreCase ? char.ToLowerInvariant(ch) : ch;
                if (!node.Next.TryGetValue(key, out Node? child))
                {
                    child = new Node();
                    node.Next[key] = child;
                }
                node = child;
            }
            node.Style = rule.Style;
            node.IgnoreCase = rule.IgnoreCase;
        }

        public bool TryMatch(string word, out SyntaxStyle? style)
        {
            style = null;
            Node node = _root;

            // try exact first
            if (Walk(word, caseSensitive: true, ref node) && node.Style is not null)
            {
                style = node.Style;
                return true;
            }

            node = _root;
            if (Walk(word, caseSensitive: false, ref node) && node is { Style: not null, IgnoreCase: true })
            {
                style = node.Style;
                return true;
            }

            return false;
        }

        private static bool Walk(string word, bool caseSensitive, ref Node node)
        {
            foreach (char ch in word)
            {
                char key = caseSensitive ? ch : char.ToLowerInvariant(ch);
                if (!node.Next.TryGetValue(key, out Node? next))
                    return false;
                node = next;
            }
            return true;
        }

        private sealed class Node
        {
            public Dictionary<char, Node> Next { get; } = new();
            public SyntaxStyle? Style { get; set; }
            public bool IgnoreCase { get; set; }
        }
    }
}
