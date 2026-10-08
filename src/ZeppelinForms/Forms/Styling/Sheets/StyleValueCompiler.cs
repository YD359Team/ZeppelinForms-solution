using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using ZeppelinForms.Drawing.Helpers;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Theming;

namespace ZeppelinForms.Forms.Styling;

/// <summary>A compiled style sheet value: evaluated once when it doesn't mention
/// the theme, on every application with the theme at hand when it does.</summary>
internal abstract class ValueExpr
{
    public abstract bool UsesTheme { get; }

    public abstract object? Evaluate(Theme? theme);

    public static ValueExpr Constant(object? value) => new ConstantExpr(value);

    private sealed class ConstantExpr(object? value) : ValueExpr
    {
        public override bool UsesTheme => false;

        public override object? Evaluate(Theme? theme) => value;
    }
}

/// <summary>
/// Turns the text of a declaration's value into a value of the property's type.
/// The rule is that of the C# a value would be written with: a struct takes the
/// arguments of one of its constructors, in their order — <c>Padding: 14 6</c> is
/// <c>new Thickness(14, 6)</c>, <c>BoxShadow: 0 2 8 0 #0003</c> is
/// <c>new BoxShadow(0, 2, 8, 0, …)</c>. A bare name is a static member of the type
/// (<c>BoxShadow: Medium</c>), a member of an enum, or a named color.
/// </summary>
/// <remarks>
/// <para>Besides that:</para>
/// <list type="bullet">
/// <item>colors: <c>#rgb</c>, <c>#rgba</c>, <c>#rrggbb</c>, <c>#rrggbbaa</c>,
/// <c>rgb(r, g, b)</c>, <c>rgba(r, g, b, a)</c> with alpha from 0 to 1, a name from
/// <see cref="Colors"/>, and the functions <c>alpha(c, a)</c>, <c>lighten(c, x)</c>,
/// <c>darken(c, x)</c>, <c>mix(c1, c2, t)</c></item>
/// <item><c>@Accent</c> — a color role of the theme; <c>@Metrics.ControlCornerRadius</c>,
/// <c>@TypeRamp.Body.Size</c> — any path from <see cref="Theme"/></item>
/// <item>numbers with an optional <c>px</c>; <c>none</c> for a value that may be null</item>
/// </list>
/// </remarks>
internal static class StyleValueCompiler
{
    public static ValueExpr? Compile(string text, Type target, out string? error)
    {
        List<string> tokens = SplitTopLevel(text.Trim(), ' ');

        if (tokens.Count == 0)
        {
            error = "A value is missing";
            return null;
        }

        return Compile(tokens, target, out error);
    }

    private static ValueExpr? Compile(List<string> tokens, Type target, out string? error)
    {
        error = null;

        // a value that may be null: none — or the value of the underlying type
        Type? underlying = Nullable.GetUnderlyingType(target);
        bool nullable = underlying is not null || !target.IsValueType;

        if (nullable && tokens.Count == 1 && tokens[0] is "none" or "null")
            return ValueExpr.Constant(null);

        Type type = underlying ?? target;

        if (tokens.Count == 1)
            return CompileSingle(tokens[0], type, out error);

        // several tokens: the arguments of a constructor
        return CompileConstructor(tokens, type, out error);
    }

    private static ValueExpr? CompileSingle(string token, Type type, out string? error)
    {
        error = null;

        if (token.StartsWith('@'))
            return CompileThemeRef(token, type, out error);

        if (type == typeof(Color))
            return CompileColor(token, out error);

        if (type == typeof(string))
            return ValueExpr.Constant(Unquote(token));

        if (type == typeof(bool))
        {
            if (bool.TryParse(token, out bool flag)) return ValueExpr.Constant(flag);

            error = $"'{token}' is not true or false";
            return null;
        }

        if (IsNumeric(type))
        {
            if (TryParseNumber(token, out double number))
                return ValueExpr.Constant(Convert.ChangeType(number, type, CultureInfo.InvariantCulture));

            error = $"'{token}' is not a number";
            return null;
        }

        if (type.IsEnum)
        {
            if (Enum.TryParse(type, KebabToPascal(token), ignoreCase: true, out object? member))
                return ValueExpr.Constant(member);

            error = $"'{token}' is not one of {string.Join(", ", Enum.GetNames(type))}";
            return null;
        }

        // a static member of the type: BoxShadow.Medium, Font.Monospace
        if (IsIdentifier(token) && FindStatic(type, KebabToPascal(token)) is { } named)
            return ValueExpr.Constant(named);

        // a one-argument constructor: Thickness(8), CornerRadius(4)
        return CompileConstructor([token], type, out error);
    }

    // ===== constructors =====

    private static ValueExpr? CompileConstructor(List<string> tokens, Type type, out string? error)
    {
        error = null;

        ConstructorInfo[] constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        // exact arity first, then constructors whose optional parameters fill the rest
        IEnumerable<ConstructorInfo> candidates = constructors
            .Where(c => Fits(c, tokens.Count))
            .OrderBy(c => c.GetParameters().Length == tokens.Count ? 0 : 1)
            .ThenBy(c => c.GetParameters().Length);

        string? firstError = null;

        foreach (ConstructorInfo constructor in candidates)
        {
            ParameterInfo[] parameters = constructor.GetParameters();
            var arguments = new ValueExpr[parameters.Length];
            bool ok = true;

            for (int i = 0; i < parameters.Length; i++)
            {
                if (i >= tokens.Count)
                {
                    arguments[i] = ValueExpr.Constant(parameters[i].DefaultValue);
                    continue;
                }

                ValueExpr? argument = Compile([tokens[i]], parameters[i].ParameterType, out string? argumentError);

                if (argument is null)
                {
                    firstError ??= $"{parameters[i].Name}: {argumentError}";
                    ok = false;
                    break;
                }

                arguments[i] = argument;
            }

            if (!ok) continue;

            return Construct(constructor, arguments);
        }

        error = firstError ?? (constructors.Length == 0
            ? $"{type.Name} can't be written in a style sheet"
            : $"{type.Name} takes {Arities(constructors)} values, not {tokens.Count}");

        return null;
    }

    private static bool Fits(ConstructorInfo constructor, int count)
    {
        ParameterInfo[] parameters = constructor.GetParameters();

        if (parameters.Length == 0 || count > parameters.Length) return false;

        int required = parameters.Count(p => !p.IsOptional);

        return count >= required;
    }

    private static string Arities(ConstructorInfo[] constructors) =>
        string.Join(" or ", constructors
            .Select(c => c.GetParameters().Length)
            .Where(n => n > 0)
            .Distinct()
            .Order());

    private static ValueExpr Construct(ConstructorInfo constructor, ValueExpr[] arguments)
    {
        if (arguments.All(a => !a.UsesTheme))
            return ValueExpr.Constant(constructor.Invoke([.. arguments.Select(a => a.Evaluate(null))]));

        return new ThemedExpr(theme => constructor.Invoke([.. arguments.Select(a => a.Evaluate(theme))]));
    }

    // ===== colors =====

    private static ValueExpr? CompileColor(string token, out string? error)
    {
        error = null;

        if (token.StartsWith('@'))
            return CompileThemeRef(token, typeof(Color), out error);

        if (token.StartsWith('#'))
        {
            if (TryParseHex(token, out Color hex)) return ValueExpr.Constant(hex);

            error = $"'{token}' is not a color: #rgb, #rgba, #rrggbb or #rrggbbaa";
            return null;
        }

        int open = token.IndexOf('(');

        if (open > 0 && token.EndsWith(')'))
            return CompileColorFunction(token[..open].ToLowerInvariant(), token[(open + 1)..^1], out error);

        if (token.Equals("transparent", StringComparison.OrdinalIgnoreCase))
            return ValueExpr.Constant(Colors.Transparent);

        if (FindStatic(typeof(Colors), KebabToPascal(token), typeof(Color)) is Color named)
            return ValueExpr.Constant(named);

        error = $"'{token}' is not a color";
        return null;
    }

    private static ValueExpr? CompileColorFunction(string name, string inner, out string? error)
    {
        error = null;
        List<string> args = SplitTopLevel(inner, ',');

        switch (name)
        {
            case "rgb" or "rgba" when args.Count is 3 or 4:
                {
                    var channels = new double[args.Count];

                    for (int i = 0; i < args.Count; i++)
                    {
                        if (!TryParseNumber(args[i], out channels[i]))
                        {
                            error = $"'{args[i]}' is not a number";
                            return null;
                        }
                    }

                    byte a = args.Count == 4 ? ToByte(channels[3] * 255) : (byte)255;

                    return ValueExpr.Constant(new Color(a, ToByte(channels[0]), ToByte(channels[1]), ToByte(channels[2])));
                }

            case "alpha" when args.Count == 2:
                return ColorAnd(args, (c, x) => c.WithA(ToByte(x * 255)), out error);

            case "lighten" when args.Count == 2:
                return ColorAnd(args, (c, x) => c.Lighten((float)x), out error);

            case "darken" when args.Count == 2:
                return ColorAnd(args, (c, x) => c.Darken((float)x), out error);

            case "mix" when args.Count == 3:
                {
                    ValueExpr? first = CompileColor(args[0], out error);
                    if (first is null) return null;

                    ValueExpr? second = CompileColor(args[1], out error);
                    if (second is null) return null;

                    if (!TryParseNumber(args[2], out double t))
                    {
                        error = $"'{args[2]}' is not a number";
                        return null;
                    }

                    return Combine([first, second], values => Color.Lerp((Color)values[0]!, (Color)values[1]!, (float)t));
                }
        }

        error = $"Unknown color function {name}() or a wrong number of arguments";
        return null;
    }

    private static ValueExpr? ColorAnd(List<string> args, Func<Color, double, Color> apply, out string? error)
    {
        ValueExpr? color = CompileColor(args[0], out error);
        if (color is null) return null;

        if (!TryParseNumber(args[1], out double amount))
        {
            error = $"'{args[1]}' is not a number";
            return null;
        }

        return Combine([color], values => apply((Color)values[0]!, amount));
    }

    private static ValueExpr Combine(ValueExpr[] parts, Func<object?[], object?> combine)
    {
        if (parts.All(p => !p.UsesTheme))
            return ValueExpr.Constant(combine([.. parts.Select(p => p.Evaluate(null))]));

        return new ThemedExpr(theme => combine([.. parts.Select(p => p.Evaluate(theme))]));
    }

    private static bool TryParseHex(string token, out Color color)
    {
        color = default;
        string hex = token[1..];

        if (hex.Length is 3 or 4)
            hex = string.Concat(hex.Select(c => new string(c, 2)));

        if (hex.Length is not (6 or 8) ||
            !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value))
            return false;

        // CSS order: the alpha comes last
        if (hex.Length == 6)
            color = new Color(255, (byte)(value >> 16), (byte)(value >> 8), (byte)value);
        else
            color = new Color((byte)value, (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8));

        return true;
    }

    private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);

    // ===== theme references =====

    private static readonly ConcurrentDictionary<string, (Func<Theme, object?> Get, Type Type)?> ThemePaths = new();

    private static ValueExpr? CompileThemeRef(string token, Type target, out string? error)
    {
        error = null;
        string path = token[1..];

        if (ResolveThemePath(path) is not { } resolved)
        {
            error = $"The theme has no '{path}'";
            return null;
        }

        Type nonNullable = Nullable.GetUnderlyingType(target) ?? target;
        Type source = Nullable.GetUnderlyingType(resolved.Type) ?? resolved.Type;

        if (nonNullable.IsAssignableFrom(source))
            return new ThemedExpr(resolved.Get);

        if (IsNumeric(nonNullable) && IsNumeric(source))
            return new ThemedExpr(theme =>
                resolved.Get(theme) is { } value
                    ? Convert.ChangeType(value, nonNullable, CultureInfo.InvariantCulture)
                    : null);

        error = $"'{path}' is {resolved.Type.Name}, not {target.Name}";
        return null;
    }

    /// <summary>A path from <see cref="Theme"/> — or, for a first segment the theme
    /// itself doesn't have, from its colors: <c>@Accent</c> is <c>@Colors.Accent</c>.</summary>
    private static (Func<Theme, object?> Get, Type Type)? ResolveThemePath(string path) =>
        ThemePaths.GetOrAdd(path, static path =>
        {
            string[] segments = path.Split('.');

            if (Walk(typeof(Theme), segments) is { } direct)
            {
                Func<object?, object?> get = direct.Get;
                return (theme => get(theme), direct.Type);
            }

            if (Walk(typeof(ThemeColors), segments) is { } color)
            {
                Func<object?, object?> get = color.Get;
                return (theme => get(theme.Colors), color.Type);
            }

            return null;
        });

    private static (Func<object?, object?> Get, Type Type)? Walk(Type start, string[] segments)
    {
        Type type = start;
        var getters = new List<PropertyInfo>();

        foreach (string segment in segments)
        {
            PropertyInfo? property = type.GetProperty(
                KebabToPascal(segment),
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (property is null || property.GetIndexParameters().Length > 0) return null;

            getters.Add(property);
            type = property.PropertyType;
        }

        return (target =>
        {
            object? current = target;

            foreach (PropertyInfo getter in getters)
            {
                if (current is null) return null;
                current = getter.GetValue(current);
            }

            return current;
        }, type);
    }

    private sealed class ThemedExpr(Func<Theme, object?> evaluate) : ValueExpr
    {
        public override bool UsesTheme => true;

        public override object? Evaluate(Theme? theme) =>
            theme is null ? null : evaluate(theme);
    }

    // ===== helpers =====

    internal static bool IsNumeric(Type type) =>
        type == typeof(float) || type == typeof(double) || type == typeof(int) ||
        type == typeof(long) || type == typeof(short) || type == typeof(byte) ||
        type == typeof(decimal) || type == typeof(uint);

    private static bool TryParseNumber(string token, out double number)
    {
        string text = token.Trim();

        if (text.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            text = text[..^2];

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }

    private static bool IsIdentifier(string token) =>
        token.Length > 0 && (char.IsLetter(token[0]) || token[0] == '_') &&
        token.All(c => char.IsLetterOrDigit(c) || c is '_' or '-');

    private static object? FindStatic(Type type, string name, Type? valueType = null)
    {
        valueType ??= type;

        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase;

        if (type.GetProperty(name, flags) is { } property &&
            valueType.IsAssignableFrom(property.PropertyType) && property.GetIndexParameters().Length == 0)
            return property.GetValue(null);

        if (type.GetField(name, flags) is { } field && valueType.IsAssignableFrom(field.FieldType))
            return field.GetValue(null);

        return null;
    }

    /// <summary>"ease-in-out" → "EaseInOut", "background-color" → "BackgroundColor";
    /// a name already in Pascal case is left as it is.</summary>
    internal static string KebabToPascal(string name)
    {
        if (!name.Contains('-')) return name.Length > 0 && char.IsLower(name[0])
            ? char.ToUpperInvariant(name[0]) + name[1..]
            : name;

        return string.Concat(name
            .Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    internal static string Unquote(string token) =>
        token.Length >= 2 && token[0] is '"' or '\'' && token[^1] == token[0]
            ? token[1..^1]
            : token;

    /// <summary>Split at a separator outside parentheses and quotes; empty parts are
    /// dropped and the rest trimmed.</summary>
    internal static List<string> SplitTopLevel(string text, char separator)
    {
        var parts = new List<string>();
        int depth = 0;
        char quote = '\0';
        int start = 0;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                continue;
            }

            switch (c)
            {
                case '"' or '\'':
                    quote = c;
                    break;

                case '(':
                    depth++;
                    break;

                case ')':
                    depth--;
                    break;

                default:
                    bool split = depth == 0 && (separator == ' ' ? char.IsWhiteSpace(c) : c == separator);

                    if (split)
                    {
                        Add(text[start..i]);
                        start = i + 1;
                    }

                    break;
            }
        }

        Add(text[start..]);
        return parts;

        void Add(string part)
        {
            part = part.Trim();
            if (part.Length > 0) parts.Add(part);
        }
    }
}