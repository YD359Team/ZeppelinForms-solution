using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using ZeppelinForms.Animation;

namespace ZeppelinForms.Forms.Styling;

/// <summary>Reads the text of a <c>.zss</c> sheet into styles, see <see cref="StyleSheet"/>.
/// One parser serves a sheet and all its imports: they share the styles, the
/// diagnostics, the variables and the set of files already read.</summary>
internal sealed partial class StyleSheetParser(string baseDirectory)
{
    public List<Style> Styles { get; } = [];
    public List<StyleDiagnostic> Diagnostics { get; } = [];
    public List<string> Files { get; } = [];

    private readonly Dictionary<string, string> _variables = new(StringComparer.Ordinal);

    /// <summary>Files on the current import chain: a sheet importing itself, directly
    /// or through others, is reported instead of read forever.</summary>
    private readonly Stack<string> _importChain = new();

    public void ParseFile(string path)
    {
        string text;

        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Diagnostics.Add(new StyleDiagnostic(StyleDiagnosticSeverity.Error,
                $"Can't read the file: {e.Message}", path, 1, 1));
            return;
        }

        Files.Add(path);
        _importChain.Push(path);

        try
        {
            new Reader(this, text, path, System.IO.Path.GetDirectoryName(path)!).ParseSheet();
        }
        finally
        {
            _importChain.Pop();
        }
    }

    public void ParseText(string text, string sourceName) =>
        new Reader(this, text, sourceName, baseDirectory).ParseSheet();

    /// <summary>The reading of one text: a sheet or an import.</summary>
    private sealed partial class Reader
    {
        private readonly StyleSheetParser _owner;
        private readonly string _text;
        private readonly string _source;
        private readonly string _directory;
        private readonly int[] _lineStarts;
        private int _pos;

        public Reader(StyleSheetParser owner, string text, string source, string directory)
        {
            _owner = owner;
            _source = source;
            _directory = directory;

            // comments become spaces: the offsets, and with them lines and columns,
            // stay those of the original text
            _text = StripComments(text);

            var starts = new List<int> { 0 };
            for (int i = 0; i < _text.Length; i++)
                if (_text[i] == '\n') starts.Add(i + 1);

            _lineStarts = [.. starts];
        }

        private bool AtEnd => _pos >= _text.Length;

        private char Peek => AtEnd ? '\0' : _text[_pos];

        public void ParseSheet()
        {
            while (true)
            {
                SkipSpace();

                if (AtEnd) return;

                if (Peek == '}')
                {
                    Report(StyleDiagnosticSeverity.Error, _pos, "Unexpected '}'");
                    _pos++;
                    continue;
                }

                if (_text.AsSpan(_pos).StartsWith("@import", StringComparison.Ordinal))
                {
                    ParseImport();
                    continue;
                }

                if (Peek == '$')
                {
                    ParseVariable();
                    continue;
                }

                ParseRule(parents: null);
            }
        }

        // ===== statements =====

        private void ParseImport()
        {
            int start = _pos;
            int end = StatementEnd();
            string argument = _text[(start + "@import".Length)..end].Trim();

            _pos = Math.Min(end + 1, _text.Length);

            string relative = StyleValueCompiler.Unquote(argument);

            if (relative.Length == 0 || relative == argument)
            {
                Report(StyleDiagnosticSeverity.Error, start, "@import takes a quoted file name: @import \"buttons.zss\";");
                return;
            }

            string path = Path.GetFullPath(Path.Combine(_directory, relative));

            if (_owner._importChain.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                Report(StyleDiagnosticSeverity.Error, start, $"'{relative}' imports itself");
                return;
            }

            if (!File.Exists(path))
            {
                Report(StyleDiagnosticSeverity.Error, start, $"'{relative}' is not found next to {Path.GetFileName(_source)}");
                return;
            }

            _owner.ParseFile(path);
        }

        private void ParseVariable()
        {
            int start = _pos;
            int end = StatementEnd();
            string statement = _text[start..end];

            _pos = Math.Min(end + 1, _text.Length);

            int colon = statement.IndexOf(':');
            string name = colon > 0 ? statement[1..colon].Trim() : string.Empty;

            if (colon < 0 || !VariableName().IsMatch(name))
            {
                Report(StyleDiagnosticSeverity.Error, start, "A variable is written $name: value;");
                return;
            }

            string? value = Substitute(statement[(colon + 1)..].Trim(), start + colon + 1);

            if (value is null) return;

            _owner._variables[name] = value;
        }

        /// <param name="parents">The enclosing rule's selector alternatives, for a nested rule.</param>
        private void ParseRule(List<string>? parents)
        {
            int start = _pos;
            int brace = StatementEnd();

            if (brace >= _text.Length || _text[brace] != '{')
            {
                Report(StyleDiagnosticSeverity.Error, start,
                    brace >= _text.Length ? "A rule needs a block in braces" : $"Unexpected '{_text[brace]}': a rule needs '{{'");

                _pos = Math.Min(brace + 1, _text.Length);
                return;
            }

            string written = _text[start..brace];
            int leading = written.Length - written.TrimStart().Length;
            _pos = brace + 1;

            List<string> alternatives = Combine(parents, written.Trim());
            string text = string.Join(", ", alternatives);

            HashSet<PseudoClass> before = [.. PseudoClass.Registered];

            if (!Selector.TryParse(text, out Selector? selector, out SelectorSyntaxException? error))
            {
                // a position inside a combined nested selector would point into text
                // the author never wrote; the start of the rule is the honest place
                int at = parents is null ? start + leading + error!.Position : start + leading;
                Report(StyleDiagnosticSeverity.Error, at, error!.Reason);

                SkipBlock();
                return;
            }

            CheckSelector(selector!, before, start + leading);

            var style = new Style(selector!) { Source = $"{_source}:{LineOf(start + leading)}" };

            // added before the nested rules: they come later in the cascade order
            int index = _owner.Styles.Count;
            _owner.Styles.Add(style);

            ParseBlock(style, alternatives);

            // a rule that only holds nested ones has nothing to apply
            if (style.Setters.Count == 0 && style.Transitions.Count == 0)
                _owner.Styles.RemoveAt(index);
        }

        private void ParseBlock(Style style, List<string> alternatives)
        {
            while (true)
            {
                SkipSpace();

                if (AtEnd)
                {
                    Report(StyleDiagnosticSeverity.Error, _pos, "'}' is missing");
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
                    ParseRule(alternatives);
                    continue;
                }

                ParseDeclaration(style, start, end);

                // the closing brace belongs to the block
                _pos = end < _text.Length && _text[end] == ';' ? end + 1 : end;
            }
        }

        private void ParseDeclaration(Style style, int start, int end)
        {
            string statement = _text[start..end];
            int colon = statement.IndexOf(':');

            if (colon <= 0)
            {
                Report(StyleDiagnosticSeverity.Error, start, "A declaration is written Property: value;");
                return;
            }

            string name = statement[..colon].Trim();
            int valueAt = start + colon + 1;
            string? value = Substitute(statement[(colon + 1)..].Trim(), valueAt);

            if (value is null) return;

            if (value.Length == 0)
            {
                Report(StyleDiagnosticSeverity.Error, valueAt, $"{name} has no value");
                return;
            }

            if (name.Equals("transition", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("transitions", StringComparison.OrdinalIgnoreCase))
            {
                ParseTransitions(style, value, valueAt);
                return;
            }

            string propertyName = StyleValueCompiler.KebabToPascal(name);
            StyledProperty[] candidates = Candidates(style.Selector, propertyName);

            if (candidates.Length == 0)
            {
                Report(StyleDiagnosticSeverity.Error, start, NoProperty(style.Selector, propertyName));
                return;
            }

            var compiled = new Dictionary<Type, ValueExpr>();
            string? firstError = null;

            foreach (Type valueType in candidates.Select(p => p.ValueType).Distinct())
            {
                if (StyleValueCompiler.Compile(value, valueType, out string? error) is { } expr)
                    compiled[valueType] = expr;
                else
                    firstError ??= error;
            }

            if (compiled.Count == 0)
            {
                Report(StyleDiagnosticSeverity.Error, SkipSpaceFrom(valueAt), $"{propertyName}: {firstError}");
                return;
            }

            style.Add(new SheetSetter(candidates, value, compiled));
        }

        private void ParseTransitions(Style style, string value, int at)
        {
            foreach (string item in StyleValueCompiler.SplitTopLevel(value, ','))
            {
                List<string> parts = StyleValueCompiler.SplitTopLevel(item, ' ');
                string propertyName = StyleValueCompiler.KebabToPascal(parts[0]);

                if (parts.Count < 2 || !TryParseDuration(parts[1], out TimeSpan duration))
                {
                    Report(StyleDiagnosticSeverity.Error, at,
                        $"A transition is written Property duration [easing]: '{item}'");
                    continue;
                }

                // without an easing — the framework's default, ease-out
                Func<float, float>? easing = parts.Count > 2 ? FindEasing(parts[2]) : null;

                if (parts.Count > 2 && easing is null)
                {
                    Report(StyleDiagnosticSeverity.Error, at,
                        $"Unknown easing '{parts[2]}': linear, ease-in, ease-out, ease-in-out");
                    continue;
                }

                StyledProperty[] candidates = Candidates(style.Selector, propertyName);

                if (candidates.Length == 0)
                {
                    Report(StyleDiagnosticSeverity.Error, at, NoProperty(style.Selector, propertyName));
                    continue;
                }

                // every property of the name: the transition is looked up by reference,
                // and the element has the one of its own type
                foreach (StyledProperty property in candidates)
                    style.Transitions.Add(Transition.Ease(property, duration, easing));
            }
        }

        // ===== checks =====

        private void CheckSelector(Selector selector, HashSet<PseudoClass> before, int at)
        {
            StyleTypes.Scan();

            var namedHere = new List<PseudoClass>();

            foreach (ComplexSelector complex in selector.Alternatives)
                foreach (CompoundSelector compound in complex.Compounds)
                    Check(compound);

            StyleTypes.NoteNamedBySheet(namedHere);

            void Check(CompoundSelector compound)
            {
                if (compound.TypeName is { } type && StyleTypes.Find(type).Count == 0)
                    Report(StyleDiagnosticSeverity.Warning, at, $"No control is called '{type}': the selector never matches");

                foreach (PseudoClass pseudo in compound.PseudoClasses)
                {
                    if (!before.Contains(pseudo)) namedHere.Add(pseudo);

                    if (!before.Contains(pseudo) || StyleTypes.IsNamedOnlyBySheets(pseudo))
                        Report(StyleDiagnosticSeverity.Warning, at, $"No control raises '{pseudo}': the selector never matches");
                }

                foreach (CompoundSelector[] negation in compound.Negations)
                    foreach (CompoundSelector inner in negation)
                        Check(inner);
            }
        }

        /// <summary>The properties with this name the selector can meet: of the subject's
        /// types, their bases and the types derived from them. A subject without a type
        /// can meet anything.</summary>
        private static StyledProperty[] Candidates(Selector selector, string name)
        {
            StyledProperty[] all = StyleTypes.PropertiesNamed(name);

            if (all.Length == 0) return all;

            var types = new List<Type>();

            foreach (ComplexSelector complex in selector.Alternatives)
            {
                if (complex.Subject.TypeName is not { } typeName) return all;

                types.AddRange(StyleTypes.Find(typeName));
            }

            // an unknown type is reported on its own; the selector never matches anyway
            if (types.Count == 0) return all;

            return [.. all.Where(p => types.Any(t => p.OwnerType.IsAssignableFrom(t) || t.IsAssignableFrom(p.OwnerType)))];
        }

        private static string NoProperty(Selector selector, string name)
        {
            string? subject = selector.Alternatives.Length == 1 ? selector.Alternatives[0].Subject.TypeName : null;

            return StyleTypes.PropertiesNamed(name).Length == 0
                ? $"No control has a property '{name}'"
                : $"{subject ?? "The selected elements"} has no property '{name}'";
        }

        // ===== values =====

        /// <summary>Replace the variables in a value; null — one is unknown, and that
        /// is reported.</summary>
        private string? Substitute(string value, int at)
        {
            string? missing = null;

            string result = VariableReference().Replace(value, match =>
            {
                if (_owner._variables.TryGetValue(match.Groups[1].Value, out string? known))
                    return known;

                missing ??= match.Value;
                return match.Value;
            });

            if (missing is null) return result;

            Report(StyleDiagnosticSeverity.Error, SkipSpaceFrom(at), $"Unknown variable {missing}: declare it above with {missing}: value;");
            return null;
        }

        private static bool TryParseDuration(string text, out TimeSpan duration)
        {
            duration = default;
            double factor = 1;

            if (text.EndsWith("ms", StringComparison.OrdinalIgnoreCase)) text = text[..^2];
            else if (text.EndsWith('s')) { text = text[..^1]; factor = 1000; }

            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || value < 0)
                return false;

            duration = TimeSpan.FromMilliseconds(value * factor);
            return true;
        }

        private static Func<float, float>? FindEasing(string name)
        {
            MethodInfo? method = typeof(Easing).GetMethod(
                StyleValueCompiler.KebabToPascal(name),
                BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase,
                [typeof(float)]);

            return method is not null && method.ReturnType == typeof(float)
                ? method.CreateDelegate<Func<float, float>>()
                : null;
        }

        // ===== nesting =====

        /// <summary>The alternatives of a nested selector: <c>&amp;</c> is replaced by
        /// each of the parent's alternatives, and a selector without it is a descendant.</summary>
        private static List<string> Combine(List<string>? parents, string written)
        {
            List<string> own = StyleValueCompiler.SplitTopLevel(written, ',');

            if (parents is null) return own;

            var combined = new List<string>();

            foreach (string parent in parents)
                foreach (string child in own)
                    combined.Add(child.Contains('&') ? child.Replace("&", parent, StringComparison.Ordinal) : $"{parent} {child}");

            return combined;
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

        /// <summary>Skip a block whose header could not be read, nested blocks included.</summary>
        private void SkipBlock()
        {
            int depth = 1;

            while (!AtEnd && depth > 0)
            {
                char c = _text[_pos++];

                if (c == '{') depth++;
                else if (c == '}') depth--;
            }
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

        private int LineOf(int offset)
        {
            int line = Array.BinarySearch(_lineStarts, offset);
            return (line >= 0 ? line : ~line - 1) + 1;
        }

        private void Report(StyleDiagnosticSeverity severity, int offset, string message)
        {
            offset = Math.Clamp(offset, 0, _text.Length);

            int line = LineOf(offset);
            int column = offset - _lineStarts[line - 1] + 1;

            _owner.Diagnostics.Add(new StyleDiagnostic(severity, message, _source, line, column));
        }

        /// <summary>Comments replaced by spaces, newlines kept: <c>/* … */</c> and
        /// <c>// …</c> outside quotes. <c>//</c> right after a colon is a URL's, not
        /// a comment — not that values have URLs yet, but a sheet should not break
        /// the day they do.</summary>
        private static string StripComments(string text)
        {
            var result = new StringBuilder(text.Length);
            char quote = '\0';

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (quote != '\0')
                {
                    if (c == quote) quote = '\0';
                    result.Append(c);
                    continue;
                }

                if (c is '"' or '\'')
                {
                    quote = c;
                    result.Append(c);
                    continue;
                }

                if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    int close = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    int stop = close < 0 ? text.Length : close + 2;

                    for (; i < stop; i++)
                        result.Append(text[i] == '\n' ? '\n' : ' ');

                    i--;
                    continue;
                }

                if (c == '/' && i + 1 < text.Length && text[i + 1] == '/' && (i == 0 || text[i - 1] != ':'))
                {
                    for (; i < text.Length && text[i] != '\n'; i++)
                        result.Append(' ');

                    i--;
                    continue;
                }

                result.Append(c);
            }

            return result.ToString();
        }

        [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_-]*$")]
        private static partial Regex VariableName();

        [GeneratedRegex(@"\$([A-Za-z_][A-Za-z0-9_-]*)")]
        private static partial Regex VariableReference();
    }
}