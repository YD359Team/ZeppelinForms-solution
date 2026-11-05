using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Core.Text.Syntax;

/// <summary>How a matched token is drawn.</summary>
public sealed class SyntaxStyle
{
    public Color? Foreground { get; init; }
    public Color? Background { get; init; }
    public bool Bold { get; init; }
    public bool Italic { get; init; }

    public static SyntaxStyle Of(Color foreground, bool bold = false, bool italic = false) =>
        new() { Foreground = foreground, Bold = bold, Italic = italic };
}

/// <summary>A fixed keyword or operator matched by exact text (case policy below).</summary>
public sealed class KeywordRule
{
    public required string Lexeme { get; init; }
    public required SyntaxStyle Style { get; init; }

    /// <summary>When false, <c>If</c> and <c>if</c> are different tokens.</summary>
    public bool IgnoreCase { get; init; }
}

/// <summary>A delimited region: string, comment, interpolation, …</summary>
/// <remarks>
/// Matched by literal open/close strings, not by regex — constant time per character
/// once the open delimiter is found. Nested pairs of the same kind are not expanded;
/// a different rule can open inside when <see cref="AllowNesting"/> is set.
/// </remarks>
public sealed class BlockRule
{
    public required string Open { get; init; }
    public required string Close { get; init; }
    public required SyntaxStyle Style { get; init; }

    /// <summary>When true, the close may appear after another open of this rule.</summary>
    public bool AllowNesting { get; init; }

    /// <summary>Escape character inside the block (e.g. <c>\</c> in strings); null — none.</summary>
    public char? Escape { get; init; }

    /// <summary>When true, a line break ends the block (line comments).</summary>
    public bool EndsAtLineBreak { get; init; }
}

/// <summary>The set of rules a <see cref="SyntaxTokenizer"/> uses for one language.</summary>
public sealed class SyntaxRuleSet
{
    public IList<KeywordRule> Keywords { get; } = new List<KeywordRule>();
    public IList<BlockRule> Blocks { get; } = new List<BlockRule>();

    /// <summary>Characters that start an identifier; default is letter or underscore.</summary>
    public Func<char, bool>? IsIdentifierStart { get; init; }

    /// <summary>Characters that continue an identifier; default is letter, digit or underscore.</summary>
    public Func<char, bool>? IsIdentifierPart { get; init; }

    public SyntaxStyle? NumberStyle { get; init; }
    public SyntaxStyle? DefaultStyle { get; init; }

    /// <summary>All keyword lexemes, for completion lists.</summary>
    public IEnumerable<string> KeywordLexemes => Keywords.Select(k => k.Lexeme).Distinct();

    public static SyntaxRuleSet CSharp()
    {
        var set = new SyntaxRuleSet
        {
            NumberStyle = SyntaxStyle.Of(new Color(255, 181, 206, 168)),
            DefaultStyle = null,
        };

        // VS-like palette on a dark editor
        Color kw = new(255, 86, 156, 214);       // keywords
        Color type = new(255, 78, 201, 176);     // type names
        Color str = new(255, 206, 145, 120);     // strings
        Color comment = new(255, 106, 153, 85);  // comments
        Color preproc = new(255, 155, 155, 155); // #directives
        Color method = new(255, 220, 220, 170);  // reserved for hosts

        string[] keywords =
        [
            "abstract", "as", "base", "break", "case", "catch", "checked", "class",
            "const", "continue", "default", "delegate", "do", "else", "enum", "event",
            "explicit", "extern", "false", "finally", "fixed", "for", "foreach", "goto",
            "if", "implicit", "in", "interface", "internal", "is", "lock", "namespace",
            "new", "null", "operator", "out", "override", "params", "private",
            "protected", "public", "readonly", "ref", "return", "sealed", "sizeof",
            "stackalloc", "static", "struct", "switch", "this", "throw", "true", "try",
            "typeof", "unchecked", "unsafe", "using", "virtual", "volatile", "while",
            "record", "async", "await", "nameof", "when", "with", "init", "required",
            "file", "scoped", "partial", "where", "get", "set", "add", "remove", "value",
            "yield", "notnull", "unmanaged", "not", "and", "or",
        ];
        foreach (string k in keywords)
            set.Keywords.Add(new KeywordRule { Lexeme = k, Style = SyntaxStyle.Of(kw) });

        string[] types =
        [
            "bool", "byte", "sbyte", "char", "decimal", "double", "float", "int", "uint",
            "long", "ulong", "short", "ushort", "object", "string", "void", "var",
            "dynamic", "nint", "nuint",
        ];
        foreach (string k in types)
            set.Keywords.Add(new KeywordRule { Lexeme = k, Style = SyntaxStyle.Of(type) });

        // Longer openers first is enforced by the tokenizer.
        set.Blocks.Add(new BlockRule
        {
            Open = "///",
            Close = "\n",
            Style = SyntaxStyle.Of(comment, italic: true),
            EndsAtLineBreak = true,
        });
        set.Blocks.Add(new BlockRule
        {
            Open = "//",
            Close = "\n",
            Style = SyntaxStyle.Of(comment, italic: true),
            EndsAtLineBreak = true,
        });
        set.Blocks.Add(new BlockRule
        {
            Open = "/*",
            Close = "*/",
            Style = SyntaxStyle.Of(comment, italic: true),
        });
        // interpolated / raw-ish openers before plain quotes
        set.Blocks.Add(new BlockRule
        {
            Open = "$@\"",
            Close = "\"",
            Style = SyntaxStyle.Of(str),
        });
        set.Blocks.Add(new BlockRule
        {
            Open = "@$\"",
            Close = "\"",
            Style = SyntaxStyle.Of(str),
        });
        set.Blocks.Add(new BlockRule
        {
            Open = "@\"",
            Close = "\"",
            Style = SyntaxStyle.Of(str),
        });
        set.Blocks.Add(new BlockRule
        {
            Open = "$\"",
            Close = "\"",
            Style = SyntaxStyle.Of(str),
            Escape = '\\',
        });
        set.Blocks.Add(new BlockRule
        {
            Open = "\"",
            Close = "\"",
            Style = SyntaxStyle.Of(str),
            Escape = '\\',
        });
        set.Blocks.Add(new BlockRule
        {
            Open = "'",
            Close = "'",
            Style = SyntaxStyle.Of(str),
            Escape = '\\',
        });
        set.Blocks.Add(new BlockRule
        {
            Open = "#",
            Close = "\n",
            Style = SyntaxStyle.Of(preproc),
            EndsAtLineBreak = true,
        });

        _ = method; // reserved palette slot for a host that adds method-name rules
        return set;
    }

}

/// <summary>One coloured range produced by the tokenizer.</summary>
public readonly struct SyntaxToken
{
    public SyntaxToken(int start, int length, SyntaxStyle style)
    {
        Start = start;
        Length = length;
        Style = style;
    }

    public int Start { get; }
    public int Length { get; }
    public int End => Start + Length;
    public SyntaxStyle Style { get; }
}
