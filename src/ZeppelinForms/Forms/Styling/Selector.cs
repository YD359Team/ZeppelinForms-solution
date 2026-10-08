using System.Collections.Concurrent;
using System.Globalization;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms.Styling;

/// <summary>
/// Which elements a style applies to, written the way CSS writes it:
/// <c>Button.primary:hover</c>, <c>StackPanel.toolbar &gt; *:first-child</c>,
/// <c>#saveButton</c>, <c>ListBox, TreeView:focus-within</c>.
/// </summary>
/// <remarks>
/// <para>The grammar, a subset of CSS selectors:</para>
/// <list type="bullet">
/// <item><c>Button</c> — the type or any type derived from it, by the short class name,
/// as <see cref="Theming.Theme.For{T}(Action{T, Theming.Theme})"/> matches;
/// <c>*</c> — any element</item>
/// <item><c>.primary</c> — a class from <see cref="UIElement.Classes"/></item>
/// <item><c>#save</c> — the element's <see cref="UIElement.Name"/></item>
/// <item><c>:hover</c> — a <see cref="PseudoClass"/>; <c>:not(…)</c> — none of the
/// listed compound selectors</item>
/// <item><c>:first-child</c>, <c>:last-child</c>, <c>:only-child</c>, <c>:odd</c>,
/// <c>:even</c>, <c>:nth-child(an+b)</c>, <c>:nth-last-child(an+b)</c> — the place
/// among the siblings, counted from one</item>
/// <item><c>A B</c> — B anywhere inside A; <c>A &gt; B</c> — B directly inside A</item>
/// <item><c>A, B</c> — either</item>
/// </list>
/// <para>
/// Specificity is that of CSS: names beat classes and pseudo-classes, which beat types.
/// Of two matching styles the more specific one wins; at equal specificity — the one
/// declared closer to the element, then the one declared later.
/// </para>
/// </remarks>
public sealed class Selector
{
    /// <summary>The text the selector was parsed from, normalized.</summary>
    public string Text { get; }

    internal ComplexSelector[] Alternatives { get; }

    private Selector(string text, ComplexSelector[] alternatives)
    {
        Text = text;
        Alternatives = alternatives;
    }

    public override string ToString() => Text;

    /// <summary>Parse a selector. Throws <see cref="SelectorSyntaxException"/>
    /// with the position of the mistake.</summary>
    public static Selector Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        ComplexSelector[] alternatives = new SelectorParser(text).ParseList();

        return new Selector(
            string.Join(", ", alternatives.Select(static a => a.ToString())),
            alternatives);
    }

    /// <summary>Parse a selector without throwing.</summary>
    public static bool TryParse(string text, out Selector? selector, out SelectorSyntaxException? error)
    {
        try
        {
            selector = Parse(text);
            error = null;
            return true;
        }
        catch (SelectorSyntaxException e)
        {
            selector = null;
            error = e;
            return false;
        }
    }

    /// <summary>Whether the element matches; the specificity of the most specific
    /// alternative that does.</summary>
    public bool Matches(UIElement element, out Specificity specificity)
    {
        specificity = default;
        bool any = false;

        foreach (ComplexSelector alternative in Alternatives)
        {
            if (alternative.Specificity <= specificity && any) continue;
            if (!alternative.Matches(element)) continue;

            specificity = alternative.Specificity;
            any = true;
        }

        return any;
    }

    /// <summary>Whether the element matches.</summary>
    public bool Matches(UIElement element) => Matches(element, out _);

    /// <summary>The short type names of an element's class and its ancestors:
    /// <c>Button</c> matches a <c>SplitButton</c> too.</summary>
    private static readonly ConcurrentDictionary<Type, HashSet<string>> TypeNames = new();

    internal static bool IsOfType(UIElement element, string name) =>
        TypeNames.GetOrAdd(element.GetType(), static type =>
        {
            var names = new HashSet<string>(StringComparer.Ordinal);

            for (Type? current = type; current is not null; current = current.BaseType)
            {
                // a generic type is named without its arity suffix: Foo`1 → Foo
                string n = current.Name;
                int tick = n.IndexOf('`');
                names.Add(tick < 0 ? n : n[..tick]);
            }

            return names;
        }).Contains(name);
}

/// <summary>A selector's weight in the cascade: (names, classes and pseudo-classes, types),
/// compared in that order.</summary>
public readonly record struct Specificity(int Ids, int Classes, int Types) : IComparable<Specificity>
{
    public int CompareTo(Specificity other)
    {
        if (Ids != other.Ids) return Ids.CompareTo(other.Ids);
        if (Classes != other.Classes) return Classes.CompareTo(other.Classes);
        return Types.CompareTo(other.Types);
    }

    public static bool operator <(Specificity a, Specificity b) => a.CompareTo(b) < 0;
    public static bool operator >(Specificity a, Specificity b) => a.CompareTo(b) > 0;
    public static bool operator <=(Specificity a, Specificity b) => a.CompareTo(b) <= 0;
    public static bool operator >=(Specificity a, Specificity b) => a.CompareTo(b) >= 0;

    public static Specificity operator +(Specificity a, Specificity b) =>
        new(a.Ids + b.Ids, a.Classes + b.Classes, a.Types + b.Types);

    public override string ToString() => $"({Ids},{Classes},{Types})";
}

/// <summary>A selector that could not be parsed.</summary>
public sealed class SelectorSyntaxException(string message, string selector, int position)
    : FormatException($"{message} at position {position + 1} of \"{selector}\".")
{
    /// <summary>The selector text.</summary>
    public string Selector { get; } = selector;

    /// <summary>Zero-based position of the mistake in <see cref="Selector"/>.</summary>
    public int Position { get; } = position;

    /// <summary>The message without the position and the text.</summary>
    public string Reason { get; } = message;
}

internal enum Combinator : byte
{
    /// <summary>The left part is any ancestor: whitespace.</summary>
    Descendant,

    /// <summary>The left part is the parent: &gt;.</summary>
    Child,
}

/// <summary>A chain of compound selectors joined by combinators: <c>A &gt; B C</c>.
/// The last compound is the subject — the element the style is applied to.</summary>
internal sealed class ComplexSelector
{
    /// <summary>Left to right.</summary>
    public CompoundSelector[] Compounds { get; }

    /// <summary><c>Combinators[i]</c> joins <c>Compounds[i]</c> and <c>Compounds[i + 1]</c>.</summary>
    public Combinator[] Combinators { get; }

    public Specificity Specificity { get; }

    public CompoundSelector Subject => Compounds[^1];

    public ComplexSelector(CompoundSelector[] compounds, Combinator[] combinators)
    {
        Compounds = compounds;
        Combinators = combinators;

        Specificity total = default;
        foreach (CompoundSelector compound in compounds)
            total += compound.Specificity;

        Specificity = total;
    }

    public bool Matches(UIElement element) =>
        Compounds[^1].Matches(element) && MatchLeft(element, Compounds.Length - 1);

    /// <summary>Match the compounds left of <paramref name="index"/>, given that
    /// <paramref name="element"/> matched the one at it. Right to left, as browsers
    /// do: the subject is checked first, and most elements fail right there.</summary>
    private bool MatchLeft(UIElement element, int index)
    {
        if (index == 0) return true;

        CompoundSelector left = Compounds[index - 1];

        if (Combinators[index - 1] == Combinator.Child)
        {
            UIElement? parent = element.Parent;

            return parent is not null && left.Matches(parent) && MatchLeft(parent, index - 1);
        }

        // any ancestor will do; trying each is the backtracking CSS needs for
        // "A B C" where the nearest A doesn't lead to a matching chain but a farther one does
        for (UIElement? ancestor = element.Parent; ancestor is not null; ancestor = ancestor.Parent)
            if (left.Matches(ancestor) && MatchLeft(ancestor, index - 1))
                return true;

        return false;
    }

    public override string ToString()
    {
        var text = new System.Text.StringBuilder();

        for (int i = 0; i < Compounds.Length; i++)
        {
            if (i > 0)
                text.Append(Combinators[i - 1] == Combinator.Child ? " > " : " ");

            text.Append(Compounds[i]);
        }

        return text.ToString();
    }
}

/// <summary>Conditions on one element: <c>Button.primary#save:hover:not(.flat)</c>.</summary>
internal sealed class CompoundSelector
{
    /// <summary>Null — any type (<c>*</c> or no type at all).</summary>
    public string? TypeName { get; init; }
    public string? Id { get; init; }
    public string[] Classes { get; init; } = [];
    public PseudoClass[] PseudoClasses { get; init; } = [];
    public NthCondition[] Positions { get; init; } = [];

    /// <summary>Each <c>:not(…)</c>: the element must match none of its compounds.</summary>
    public CompoundSelector[][] Negations { get; init; } = [];

    public Specificity Specificity
    {
        get
        {
            // :only-child is stored as two conditions, the second unnamed;
            // it weighs as one pseudo-class, as in CSS
            int positions = 0;

            foreach (NthCondition position in Positions)
                if (position.Written.Length > 0) positions++;

            var own = new Specificity(
                Id is null ? 0 : 1,
                Classes.Length + PseudoClasses.Length + positions,
                TypeName is null ? 0 : 1);

            // :not takes the specificity of its most specific argument, as in CSS
            foreach (CompoundSelector[] negation in Negations)
            {
                Specificity max = default;

                foreach (CompoundSelector inner in negation)
                    if (inner.Specificity > max) max = inner.Specificity;

                own += max;
            }

            return own;
        }
    }

    public bool Matches(UIElement element)
    {
        if (TypeName is not null && !Selector.IsOfType(element, TypeName)) return false;
        if (Id is not null && !string.Equals(element.Name, Id, StringComparison.Ordinal)) return false;

        if (Classes.Length > 0)
        {
            if (element.ClassesOrNull is not { } classes) return false;

            foreach (string name in Classes)
                if (!classes.Contains(name)) return false;
        }

        foreach (PseudoClass pseudo in PseudoClasses)
            if (!element.HasPseudoClass(pseudo)) return false;

        if (Positions.Length > 0)
        {
            if (!UIElement.TryGetSiblingPosition(element, out int index, out int count))
                return false;

            foreach (NthCondition position in Positions)
                if (!position.Matches(position.FromEnd ? count - index : index + 1))
                    return false;
        }

        foreach (CompoundSelector[] negation in Negations)
            foreach (CompoundSelector inner in negation)
                if (inner.Matches(element)) return false;

        return true;
    }

    public override string ToString()
    {
        var text = new System.Text.StringBuilder();

        text.Append(TypeName);

        if (Id is not null) text.Append('#').Append(Id);

        foreach (string name in Classes) text.Append('.').Append(name);
        foreach (PseudoClass pseudo in PseudoClasses) text.Append(':').Append(pseudo.Name);
        foreach (NthCondition position in Positions) text.Append(position);

        foreach (CompoundSelector[] negation in Negations)
            text.Append(":not(").Append(string.Join(", ", negation.Select(static n => n.ToString()))).Append(')');

        return text.Length == 0 ? "*" : text.ToString();
    }
}

/// <summary>The <c>an+b</c> of <c>:nth-child</c>: the positions a·n + b for some n ≥ 0.</summary>
internal readonly record struct NthCondition(int A, int B, bool FromEnd, string Written)
{
    public bool Matches(int position)
    {
        if (A == 0) return position == B;

        // position = A·n + B with integer n ≥ 0
        int offset = position - B;

        return offset % A == 0 && offset / A >= 0;
    }

    public override string ToString() => Written;
}

/// <summary>A recursive-descent parser over the grammar described on <see cref="Selector"/>.</summary>
internal ref struct SelectorParser(string text)
{
    private readonly string _text = text;
    private int _pos;

    public ComplexSelector[] ParseList()
    {
        var list = new List<ComplexSelector>();

        SkipSpace();

        if (AtEnd) throw Error("A selector is empty");

        while (true)
        {
            list.Add(ParseComplex());
            SkipSpace();

            if (AtEnd) break;

            if (Peek != ',') throw Error($"Unexpected '{Peek}'");

            _pos++;
            SkipSpace();

            if (AtEnd) throw Error("A selector is missing after ','");
        }

        return [.. list];
    }

    private ComplexSelector ParseComplex()
    {
        var compounds = new List<CompoundSelector> { ParseCompound() };
        var combinators = new List<Combinator>();

        while (true)
        {
            bool space = SkipSpace();

            if (AtEnd || Peek is ',' or ')') break;

            if (Peek == '>')
            {
                _pos++;
                SkipSpace();
                combinators.Add(Combinator.Child);
            }
            else if (space)
            {
                combinators.Add(Combinator.Descendant);
            }
            else
            {
                throw Error($"Unexpected '{Peek}'");
            }

            if (AtEnd) throw Error("A selector is missing after a combinator");

            compounds.Add(ParseCompound());
        }

        return new ComplexSelector([.. compounds], [.. combinators]);
    }

    private CompoundSelector ParseCompound()
    {
        int start = _pos;

        string? type = null;
        string? id = null;
        var classes = new List<string>();
        var pseudo = new List<PseudoClass>();
        var positions = new List<NthCondition>();
        var negations = new List<CompoundSelector[]>();

        if (Peek == '*')
        {
            _pos++;
        }
        else if (IsIdentStart(Peek))
        {
            type = ReadIdent();
        }

        while (!AtEnd)
        {
            char c = Peek;

            if (c == '.')
            {
                _pos++;
                classes.Add(ReadIdent("a class name"));
            }
            else if (c == '#')
            {
                _pos++;

                if (id is not null) throw Error("An element has one name; a second '#' is never matched");

                id = ReadIdent("a name");
            }
            else if (c == ':')
            {
                _pos++;
                ParsePseudo(pseudo, positions, negations);
            }
            else
            {
                break;
            }
        }

        if (_pos == start) throw Error(AtEnd ? "A selector is missing" : $"Unexpected '{Peek}'");

        return new CompoundSelector
        {
            TypeName = type,
            Id = id,
            Classes = [.. classes],
            PseudoClasses = [.. pseudo],
            Positions = [.. positions],
            Negations = [.. negations],
        };
    }

    private void ParsePseudo(List<PseudoClass> pseudo, List<NthCondition> positions, List<CompoundSelector[]> negations)
    {
        int nameStart = _pos;
        string name = ReadIdent("a pseudo-class").ToLowerInvariant();

        switch (name)
        {
            case "first-child":
                positions.Add(new NthCondition(0, 1, false, ":first-child"));
                return;

            case "last-child":
                positions.Add(new NthCondition(0, 1, true, ":last-child"));
                return;

            case "only-child":
                positions.Add(new NthCondition(0, 1, false, ":only-child"));
                positions.Add(new NthCondition(0, 1, true, string.Empty));
                return;

            case "odd":
                positions.Add(new NthCondition(2, 1, false, ":odd"));
                return;

            case "even":
                positions.Add(new NthCondition(2, 2, false, ":even"));
                return;

            case "nth-child":
            case "nth-last-child":
                {
                    Expect('(');
                    int argStart = _pos;
                    int close = _text.IndexOf(')', _pos);

                    if (close < 0) throw Error("')' is missing");

                    string argument = _text[argStart..close];
                    (int a, int b) = ParseNth(argument, argStart);

                    _pos = close + 1;

                    bool fromEnd = name == "nth-last-child";
                    positions.Add(new NthCondition(a, b, fromEnd, $":{name}({argument.Trim()})"));
                    return;
                }

            case "not":
                {
                    Expect('(');
                    SkipSpace();

                    var inner = new List<CompoundSelector>();

                    while (true)
                    {
                        inner.Add(ParseCompound());
                        SkipSpace();

                        if (Peek == ',')
                        {
                            _pos++;
                            SkipSpace();
                            continue;
                        }

                        break;
                    }

                    Expect(')');
                    negations.Add([.. inner]);
                    return;
                }
        }

        if (Peek == '(')
        {
            _pos = nameStart;
            throw Error($"Unknown functional pseudo-class ':{name}()'");
        }

        pseudo.Add(PseudoClass.Register(name));
    }

    /// <summary>"odd", "even", "3", "n", "-n+3", "2n+1", "2n - 1".</summary>
    private (int A, int B) ParseNth(string argument, int offset)
    {
        string s = argument.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

        if (s == "odd") return (2, 1);
        if (s == "even") return (2, 0);

        int n = s.IndexOf('n');

        try
        {
            if (n < 0)
                return (0, int.Parse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture));

            string aPart = s[..n];
            string bPart = s[(n + 1)..];

            int a = aPart switch
            {
                "" or "+" => 1,
                "-" => -1,
                _ => int.Parse(aPart, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture),
            };

            int b = bPart.Length == 0
                ? 0
                : int.Parse(bPart, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

            return (a, b);
        }
        catch (FormatException)
        {
            _pos = offset;
            throw Error($"'{argument.Trim()}' is not of the form an+b");
        }
        catch (OverflowException)
        {
            _pos = offset;
            throw Error($"'{argument.Trim()}' is out of range");
        }
    }

    private readonly bool AtEnd => _pos >= _text.Length;

    private readonly char Peek => AtEnd ? '\0' : _text[_pos];

    private bool SkipSpace()
    {
        int start = _pos;

        while (!AtEnd && char.IsWhiteSpace(_text[_pos]))
            _pos++;

        return _pos > start;
    }

    private void Expect(char c)
    {
        if (Peek != c) throw Error($"'{c}' is expected");

        _pos++;
    }

    private static bool IsIdentStart(char c) => char.IsLetter(c) || c is '_' or '-';

    private static bool IsIdentPart(char c) => char.IsLetterOrDigit(c) || c is '_' or '-';

    private string ReadIdent(string what = "a name")
    {
        if (AtEnd || !IsIdentStart(Peek)) throw Error($"{char.ToUpperInvariant(what[0])}{what[1..]} is expected");

        int start = _pos;

        while (!AtEnd && IsIdentPart(Peek))
            _pos++;

        return _text[start.._pos];
    }

    private readonly SelectorSyntaxException Error(string message) => new(message, _text, _pos);
}