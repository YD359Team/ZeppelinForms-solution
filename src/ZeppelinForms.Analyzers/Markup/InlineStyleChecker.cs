using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace ZeppelinForms.Analyzers.Markup;

/// <summary>A problem in an inline style sheet: its offset in the sheet's text.</summary>
internal readonly struct StyleProblem(int offset, bool isError, string message)
{
    public int Offset { get; } = offset;

    /// <summary>An error stops the build; a warning is a sheet that works, but not as
    /// meant — a selector that never matches, a property nobody has.</summary>
    public bool IsError { get; } = isError;

    public string Message { get; } = message;
}

/// <summary>
/// What the build can know of an inline style sheet (<c>&lt;x:Styles&gt;</c> in markup)
/// before it runs: its shape — rules, blocks, declarations, variables — and the controls
/// and properties it names, against the compilation.
/// </summary>
/// <remarks>
/// <para>
/// It follows the run-time parser (<c>StyleSheetParser</c>) statement by statement, so
/// what it calls a broken statement is one the sheet would skip. A broken shape is an
/// error. An unknown control or property is a warning, as the sheet itself reports it: a
/// selector that never matches doesn't stop the sheet.
/// </para>
/// <para>
/// Values are left to the run time: their grammar — theme references, functions,
/// variables — is the style sheets' own, and the sheet reports a bad value with its line
/// in the markup when it is parsed.
/// </para>
/// </remarks>
internal sealed class InlineStyleChecker
{
    private static readonly Regex VariableName = new(@"^[A-Za-z_][A-Za-z0-9_-]*$", RegexOptions.CultureInvariant);
    private static readonly Regex VariableReference = new(@"\$([A-Za-z_][A-Za-z0-9_-]*)", RegexOptions.CultureInvariant);

    private readonly string _text;
    private readonly StyleNames _names;
    private readonly HashSet<string> _variables = new(StringComparer.Ordinal);
    private readonly List<StyleProblem> _problems = [];
    private int _pos;

    private InlineStyleChecker(string text, StyleNames names)
    {
        // comments become spaces: the offsets stay those of the markup
        _text = StripComments(text);
        _names = names;
    }

    public static List<StyleProblem> Check(string text, StyleNames names)
    {
        var checker = new InlineStyleChecker(text, names);
        checker.CheckSheet();

        return checker._problems;
    }

    private bool AtEnd => _pos >= _text.Length;

    private char Peek => AtEnd ? '\0' : _text[_pos];

    // ===== statements =====

    private void CheckSheet()
    {
        while (true)
        {
            SkipSpace();

            if (AtEnd) return;

            if (Peek == '}')
            {
                Error(_pos, "Unexpected '}'");
                _pos++;
                continue;
            }

            if (string.CompareOrdinal(_text, _pos, "@import", 0, 7) == 0)
            {
                CheckImport();
                continue;
            }

            if (Peek == '$')
            {
                CheckVariable();
                continue;
            }

            CheckRule(parents: null);
        }
    }

    private void CheckImport()
    {
        int start = _pos;
        int end = StatementEnd();
        string argument = _text.Substring(start + 7, end - start - 7).Trim();

        _pos = Math.Min(end + 1, _text.Length);

        bool quoted = argument.Length >= 2 &&
            (argument[0] == '"' && argument[argument.Length - 1] == '"' ||
             argument[0] == '\'' && argument[argument.Length - 1] == '\'');

        if (!quoted || argument.Length == 2)
            Error(start, "@import takes a quoted file name: @import \"buttons.zss\";");
    }

    private void CheckVariable()
    {
        int start = _pos;
        int end = StatementEnd();
        string statement = _text.Substring(start, end - start);

        _pos = Math.Min(end + 1, _text.Length);

        int colon = statement.IndexOf(':');
        string name = colon > 0 ? statement.Substring(1, colon - 1).Trim() : string.Empty;

        if (colon < 0 || !VariableName.IsMatch(name))
        {
            Error(start, "A variable is written $name: value;");
            return;
        }

        if (!Substituted(statement.Substring(colon + 1), start + colon + 1)) return;

        _variables.Add(name);
    }

    /// <param name="parents">The enclosing rule's selector alternatives, for a nested rule.</param>
    private void CheckRule(List<string>? parents)
    {
        int start = _pos;
        int brace = StatementEnd();

        if (brace >= _text.Length || _text[brace] != '{')
        {
            Error(start, brace >= _text.Length ? "A rule needs a block in braces" : "Unexpected '" + _text[brace] + "': a rule needs '{'");

            _pos = Math.Min(brace + 1, _text.Length);
            return;
        }

        string written = _text.Substring(start, brace - start);
        int at = start + (written.Length - written.TrimStart().Length);

        _pos = brace + 1;

        List<string> alternatives = Combine(parents, written.Trim());
        List<INamedTypeSymbol>? subjects = CheckSelector(alternatives, at);

        CheckBlock(alternatives, subjects);
    }

    private void CheckBlock(List<string> alternatives, List<INamedTypeSymbol>? subjects)
    {
        while (true)
        {
            SkipSpace();

            if (AtEnd)
            {
                Error(_pos, "'}' is missing");
                return;
            }

            if (Peek == '}')
            {
                _pos++;
                return;
            }

            int start = _pos;
            int end = StatementEnd();

            if (end < _text.Length && _text[end] == '{')
            {
                CheckRule(alternatives);
                continue;
            }

            CheckDeclaration(start, end, subjects);

            // the closing brace belongs to the block
            _pos = end < _text.Length && _text[end] == ';' ? end + 1 : end;
        }
    }

    private void CheckDeclaration(int start, int end, List<INamedTypeSymbol>? subjects)
    {
        string statement = _text.Substring(start, end - start);
        int colon = statement.IndexOf(':');

        if (colon <= 0)
        {
            Error(start, "A declaration is written Property: value;");
            return;
        }

        string name = statement.Substring(0, colon).Trim();
        string value = statement.Substring(colon + 1).Trim();

        if (!Substituted(value, start + colon + 1)) return;

        if (value.Length == 0)
        {
            Error(start + colon + 1, name + " has no value");
            return;
        }

        // the properties of a transition are checked by the sheet itself
        if (name.Equals("transition", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("transitions", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string property = KebabToPascal(name);
        IReadOnlyList<INamedTypeSymbol> owners = _names.OwnersOf(property);

        if (owners.Count == 0)
        {
            Warning(start, "No control has a property '" + property + "'");
            return;
        }

        // a subject without a type can meet anything
        if (subjects is null || subjects.Count == 0) return;

        bool met = owners.Any(owner => subjects.Any(subject =>
            StyleNames.Inherits(subject, owner) || StyleNames.Inherits(owner, subject)));

        if (!met)
            Warning(start, subjects[0].Name + " has no property '" + property + "'");
    }

    // ===== selectors =====

    /// <summary>Check the controls a selector names; returns the types of its subjects —
    /// the last compound of every alternative — or null when one of them has no type.</summary>
    private List<INamedTypeSymbol>? CheckSelector(List<string> alternatives, int at)
    {
        var subjects = new List<INamedTypeSymbol>();
        bool anySubject = false;

        foreach (string alternative in alternatives)
        {
            List<string> compounds = Compounds(alternative);

            if (compounds.Count == 0)
            {
                Error(at, "A selector is empty");
                return null;
            }

            for (int i = 0; i < compounds.Count; i++)
            {
                string? typeName = CheckCompound(compounds[i], at);
                bool subject = i == compounds.Count - 1;

                if (!subject) continue;

                if (typeName is null)
                {
                    anySubject = true;
                    continue;
                }

                subjects.AddRange(_names.TypesNamed(typeName));
            }
        }

        // an unknown type is reported on its own; the selector never matches anyway
        return anySubject || subjects.Count == 0 ? null : subjects;
    }

    /// <summary>The control a compound names, checked, with those of its :not(…); null
    /// when it names none.</summary>
    private string? CheckCompound(string compound, int at)
    {
        string? typeName = LeadingIdentifier(compound);

        if (typeName is not null && _names.TypesNamed(typeName).Count == 0)
            Warning(at, "No control is called '" + typeName + "': the selector never matches");

        int search = 0;

        while ((search = compound.IndexOf(":not(", search, StringComparison.Ordinal)) >= 0)
        {
            int open = search + 4;
            int close = MatchingParenthesis(compound, open);

            if (close < 0) break;

            foreach (string inner in compound.Substring(open + 1, close - open - 1).Split(','))
                foreach (string innerCompound in Compounds(inner))
                    CheckCompound(innerCompound, at);

            search = close + 1;
        }

        return typeName;
    }

    /// <summary>The compounds of one alternative: what the combinators — a space,
    /// '&gt;', '+', '~' — separate, outside parentheses and brackets.</summary>
    private static List<string> Compounds(string alternative)
    {
        var compounds = new List<string>();
        var current = new System.Text.StringBuilder();
        int depth = 0;

        foreach (char c in alternative)
        {
            if (c is '(' or '[') depth++;
            else if (c is ')' or ']') depth--;

            if (depth <= 0 && (char.IsWhiteSpace(c) || c is '>' or '+' or '~'))
            {
                if (current.Length > 0) compounds.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0) compounds.Add(current.ToString());

        return compounds;
    }

    private static string? LeadingIdentifier(string compound)
    {
        if (compound.Length == 0 || !(char.IsLetter(compound[0]) || compound[0] == '_')) return null;

        int end = 1;

        while (end < compound.Length && (char.IsLetterOrDigit(compound[end]) || compound[end] == '_'))
            end++;

        return compound.Substring(0, end);
    }

    private static int MatchingParenthesis(string text, int open)
    {
        int depth = 0;

        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')' && --depth == 0) return i;
        }

        return -1;
    }

    /// <summary>A nested selector with '&amp;' in place of its parent, or after it as a
    /// descendant — as the sheet combines them.</summary>
    private static List<string> Combine(List<string>? parents, string written)
    {
        List<string> own = SplitTopLevel(written);

        if (parents is null) return own;

        var combined = new List<string>();

        foreach (string parent in parents)
            foreach (string child in own)
                combined.Add(child.Contains("&") ? child.Replace("&", parent) : parent + " " + child);

        return combined;
    }

    private static List<string> SplitTopLevel(string text)
    {
        var parts = new List<string>();
        int depth = 0, start = 0;

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] is '(' or '[') depth++;
            else if (text[i] is ')' or ']') depth--;
            else if (text[i] == ',' && depth <= 0)
            {
                parts.Add(text.Substring(start, i - start).Trim());
                start = i + 1;
            }
        }

        parts.Add(text.Substring(start).Trim());

        return parts;
    }

    // ===== values =====

    /// <summary>Every $variable of the value is declared above. False — one isn't, and
    /// that is reported.</summary>
    private bool Substituted(string value, int at)
    {
        foreach (Match match in VariableReference.Matches(value))
        {
            if (_variables.Contains(match.Groups[1].Value)) continue;

            Error(SkipSpaceFrom(at), "Unknown variable " + match.Value + ": declare it above with " + match.Value + ": value;");
            return false;
        }

        return true;
    }

    private static string KebabToPascal(string name)
    {
        if (name.IndexOf('-') < 0)
            return name.Length > 0 && char.IsLower(name[0]) ? char.ToUpperInvariant(name[0]) + name.Substring(1) : name;

        var result = new System.Text.StringBuilder();

        foreach (string part in name.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries))
            result.Append(char.ToUpperInvariant(part[0])).Append(part.Substring(1));

        return result.ToString();
    }

    // ===== scanning =====

    /// <summary>The first '{', ';' or '}' outside quotes and parentheses, or the end.</summary>
    private int StatementEnd()
    {
        int depth = 0;
        char quote = '\0';

        for (int i = _pos; i < _text.Length; i++)
        {
            char c = _text[i];

            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                continue;
            }

            switch (c)
            {
                case '"' or '\'': quote = c; break;
                case '(': depth++; break;
                case ')': depth--; break;
                case '{' or ';' or '}' when depth <= 0: return i;
            }
        }

        return _text.Length;
    }

    private void SkipSpace()
    {
        while (!AtEnd && char.IsWhiteSpace(_text[_pos]))
            _pos++;
    }

    private int SkipSpaceFrom(int at)
    {
        while (at < _text.Length && char.IsWhiteSpace(_text[at]))
            at++;

        return at;
    }

    /// <summary>The text with its comments — <c>/* … */</c> and <c>// …</c> to the end of
    /// the line — as spaces, line breaks kept, quoted text left alone; <c>//</c> right
    /// after a colon is a URL's, as the sheet reads it.</summary>
    private static string StripComments(string text)
    {
        char[] result = text.ToCharArray();
        char quote = '\0';

        for (int i = 0; i < result.Length; i++)
        {
            char c = result[i];

            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
                continue;
            }

            if (c == '/' && i + 1 < result.Length && result[i + 1] == '*')
            {
                int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                int stop = end < 0 ? result.Length : end + 2;

                for (int j = i; j < stop; j++)
                    if (result[j] != '\n') result[j] = ' ';

                i = stop - 1;
            }
            else if (c == '/' && i + 1 < result.Length && result[i + 1] == '/')
            {
                // right after a colon it is a URL's, as the sheet reads it
                if (i > 0 && text[i - 1] == ':')
                    continue;

                int j = i;

                while (j < result.Length && result[j] != '\n')
                    result[j++] = ' ';

                i = j - 1;
            }
        }

        return new string(result);
    }

    private void Error(int offset, string message) => _problems.Add(new StyleProblem(offset, true, message));

    private void Warning(int offset, string message) => _problems.Add(new StyleProblem(offset, false, message));
}

/// <summary>The controls a style sheet can name and their styled properties, as the
/// compilation sees them: of the framework, of the project, and of the libraries built on
/// the framework that it references.</summary>
internal sealed class StyleNames
{
    private const string StyledPropertyMetadataName = "ZeppelinForms.Forms.Styling.StyledProperty`1";

    private readonly Dictionary<string, List<INamedTypeSymbol>> _types = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<INamedTypeSymbol>> _owners = new(StringComparer.Ordinal);

    private StyleNames()
    {
    }

    public IReadOnlyList<INamedTypeSymbol> TypesNamed(string name) =>
        _types.TryGetValue(name, out List<INamedTypeSymbol>? types) ? types : [];

    /// <summary>The types that declare a styled property of this name.</summary>
    public IReadOnlyList<INamedTypeSymbol> OwnersOf(string property) =>
        _owners.TryGetValue(property, out List<INamedTypeSymbol>? owners) ? owners : [];

    public static bool Inherits(INamedTypeSymbol type, INamedTypeSymbol @base)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, @base.OriginalDefinition))
                return true;

        return false;
    }

    public static StyleNames Collect(Compilation compilation, INamedTypeSymbol element)
    {
        var names = new StyleNames();
        INamedTypeSymbol? styled = compilation.GetTypeByMetadataName(StyledPropertyMetadataName);
        IAssemblySymbol framework = element.ContainingAssembly;

        var assemblies = new List<IAssemblySymbol> { compilation.Assembly };

        foreach (IAssemblySymbol referenced in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (SymbolEqualityComparer.Default.Equals(referenced, framework) ||
                referenced.Modules.Any(m => m.ReferencedAssemblies.Any(a => a.Name == framework.Name)))
            {
                assemblies.Add(referenced);
            }
        }

        foreach (IAssemblySymbol assembly in assemblies)
        {
            var namespaces = new Stack<INamespaceSymbol>();
            namespaces.Push(assembly.GlobalNamespace);

            while (namespaces.Count > 0)
            {
                INamespaceSymbol current = namespaces.Pop();

                foreach (INamespaceSymbol inner in current.GetNamespaceMembers())
                    namespaces.Push(inner);

                foreach (INamedTypeSymbol type in current.GetTypeMembers())
                {
                    if (type.TypeKind != TypeKind.Class || !Inherits(type, element)) continue;

                    Add(names._types, type.Name, type);

                    foreach (IFieldSymbol field in type.GetMembers().OfType<IFieldSymbol>())
                    {
                        if (!field.IsStatic || !field.Name.EndsWith("Property", StringComparison.Ordinal)) continue;

                        if (field.Type is not INamedTypeSymbol fieldType ||
                            !SymbolEqualityComparer.Default.Equals(fieldType.OriginalDefinition, styled))
                        {
                            continue;
                        }

                        Add(names._owners, field.Name.Substring(0, field.Name.Length - "Property".Length), type);
                    }
                }
            }
        }

        return names;
    }

    private static void Add(Dictionary<string, List<INamedTypeSymbol>> map, string key, INamedTypeSymbol type)
    {
        if (!map.TryGetValue(key, out List<INamedTypeSymbol>? list))
            map[key] = list = [];

        if (!list.Contains(type, SymbolEqualityComparer.Default))
            list.Add(type);
    }
}