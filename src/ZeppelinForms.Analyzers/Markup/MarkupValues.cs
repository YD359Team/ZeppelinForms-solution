using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ZeppelinForms.Analyzers.Markup;

/// <summary>
/// The text of an attribute as a C# expression of the property's type, by the rule of
/// the style sheets: a struct takes the arguments of one of its constructors in their
/// order — <c>Padding="14 6"</c> is <c>new Thickness(14, 6)</c> — a bare name is a
/// static member of the type or a member of an enum, a name of <c>Colors</c> (<c>red</c>
/// or <c>Colors.Red</c>), <c>MediaColors.Telegram</c> or <c>rgb(…)</c>.. Commas may 
/// separate the arguments too, as XAML writes them: <c>Size="800,600"</c>.
/// </summary>
/// <remarks>
/// Everything is decided at compile time: a value that can't be written is an error at
/// its line, not an exception when the form opens. Theme references (<c>@Accent</c>)
/// are left to style sheets, which follow a change of theme; a value in markup is set
/// once.
/// </remarks>
internal static class MarkupValues
{
    private const string ColorMetadataName = "ZeppelinForms.Drawing.Primitives.Color";
    private const string ColorsMetadataName = "ZeppelinForms.Drawing.Primitives.Colors";
    private const string MediaColorsMetadataName = "ZeppelinForms.Drawing.Primitives.MediaColors";

    public static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat;

    /// <summary>A C# expression for the text, or null with the reason in <paramref name="error"/>.</summary>
    public static string? Write(string text, ITypeSymbol type, Compilation compilation, out string? error)
    {
        error = null;
        string value = text.Trim();

        // a value that may be null
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            if (value is "none" or "null") return "null";

            return Write(value, nullable.TypeArguments[0], compilation, out error);
        }

        if (type.SpecialType == SpecialType.System_String)
            return SymbolDisplay.FormatLiteral(text, quote: true);

        if (type.SpecialType == SpecialType.System_Object)
            return SymbolDisplay.FormatLiteral(text, quote: true);

        if (value.StartsWith("@", StringComparison.Ordinal))
        {
            error = "a theme reference follows the theme only in a style sheet — give the element a class and set it there";
            return null;
        }

        if (!type.IsValueType && value is "none" or "null")
            return "null";

        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean:
                if (bool.TryParse(value, out bool flag)) return flag ? "true" : "false";
                error = "it is not true or false";
                return null;

            case SpecialType.System_Char:
                if (value.Length == 1) return SymbolDisplay.FormatLiteral(value[0], quote: true);
                error = "it is not one character";
                return null;
        }

        if (IsNumeric(type))
            return WriteNumber(value, type, out error);

        if (type.TypeKind == TypeKind.Enum)
            return WriteEnum(value, (INamedTypeSymbol)type, out error);

        if (IsType(type, ColorMetadataName))
            return WriteColor(value, compilation, out error);

        return WriteComposite(value, type, compilation, out error);
    }

    // ===== numbers =====

    private static bool IsNumeric(ITypeSymbol type) => type.SpecialType is
        SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_Int16 or
        SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32 or
        SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Single or
        SpecialType.System_Double or SpecialType.System_Decimal;

    private static string? WriteNumber(string value, ITypeSymbol type, out string? error)
    {
        error = null;

        // the style sheets' px is allowed and means nothing more
        if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            value = value.Substring(0, value.Length - 2);

        bool floating = type.SpecialType is SpecialType.System_Single or SpecialType.System_Double;

        if (floating)
        {
            string keyword = type.SpecialType == SpecialType.System_Single ? "float" : "double";

            switch (value)
            {
                case "NaN":
                case "Auto":
                case "auto":
                    return keyword + ".NaN";
                case "Infinity":
                    return keyword + ".PositiveInfinity";
                case "-Infinity":
                    return keyword + ".NegativeInfinity";
            }
        }

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
        {
            error = "it is not a number";
            return null;
        }

        string invariant = number.ToString("R", CultureInfo.InvariantCulture);

        switch (type.SpecialType)
        {
            case SpecialType.System_Single:
                return invariant + "f";
            case SpecialType.System_Double:
                return invariant + "d";
            case SpecialType.System_Decimal:
                return value + "m";
        }

        if (Math.Floor(number) != number)
        {
            error = "it is not a whole number";
            return null;
        }

        // parenthesized: (global::System.Int32)-5 would read as a subtraction
        return "((" + type.ToDisplayString(TypeFormat) + ")(" + invariant + "))";
    }

    // ===== enums =====

    private static string? WriteEnum(string value, INamedTypeSymbol type, out string? error)
    {
        error = null;

        string[] tokens = value.Split(new[] { ' ', ',', '|' }, StringSplitOptions.RemoveEmptyEntries);

        bool flags = type.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "System.FlagsAttribute");

        if (tokens.Length == 0 || (tokens.Length > 1 && !flags))
        {
            error = "one of " + string.Join(", ", Members(type)) + " is expected";
            return null;
        }

        var parts = new List<string>();

        foreach (string token in tokens)
        {
            string wanted = KebabToPascal(token);
            string? member = Members(type).FirstOrDefault(m => string.Equals(m, wanted, StringComparison.OrdinalIgnoreCase));

            if (member is null)
            {
                error = "'" + token + "' is not one of " + string.Join(", ", Members(type));
                return null;
            }

            parts.Add(type.ToDisplayString(TypeFormat) + "." + member);
        }

        return string.Join(" | ", parts);
    }

    private static IEnumerable<string> Members(INamedTypeSymbol type) =>
        type.GetMembers().OfType<IFieldSymbol>().Where(f => f.IsConst).Select(f => f.Name);

    // ===== colors =====

    private static string? WriteColor(string value, Compilation compilation, out string? error)
    {
        error = null;
        string color = "global::" + ColorMetadataName;

        if (value.StartsWith("#", StringComparison.Ordinal))
        {
            if (TryParseHex(value.Substring(1), out byte a, out byte r, out byte g, out byte b))
                return "new " + color + "(" + a + ", " + r + ", " + g + ", " + b + ")";

            error = "a color is #rgb, #rgba, #rrggbb or #rrggbbaa";
            return null;
        }

        int open = value.IndexOf('(');

        if (open > 0 && value.EndsWith(")", StringComparison.Ordinal))
        {
            string function = value.Substring(0, open).Trim().ToLowerInvariant();
            string[] args = value.Substring(open + 1, value.Length - open - 2)
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .ToArray();

            if (function is "rgb" or "rgba" && args.Length is 3 or 4 &&
                TryByte(args[0], out byte r) && TryByte(args[1], out byte g) && TryByte(args[2], out byte b))
            {
                byte a = 255;

                if (args.Length == 4)
                {
                    if (!double.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double alpha))
                    {
                        error = "the alpha of rgba is a number from 0 to 1";
                        return null;
                    }

                    a = (byte)Math.Round(Math.Max(0, Math.Min(1, alpha)) * 255);
                }

                return "new " + color + "(" + a + ", " + r + ", " + g + ", " + b + ")";
            }

            error = "of the color functions markup writes rgb(r, g, b) and rgba(r, g, b, a); the others belong in a style sheet";
            return null;
        }

        // a palette's color as C# names it: Colors.RoyalBlue, MediaColors.Telegram
        int dot = value.IndexOf('.');

        if (dot > 0 && char.IsLetter(value[0]))
        {
            string palette = value.Substring(0, dot);
            string member = value.Substring(dot + 1);

            string? metadataName =
                string.Equals(palette, "Colors", StringComparison.OrdinalIgnoreCase) ? ColorsMetadataName :
                string.Equals(palette, "MediaColors", StringComparison.OrdinalIgnoreCase) ? MediaColorsMetadataName :
                null;

            if (metadataName is null)
            {
                error = "it is not a color: a palette is Colors or MediaColors";
                return null;
            }

            if (compilation.GetTypeByMetadataName(metadataName) is { } type && StaticMember(type, member) is { } found)
                return "global::" + metadataName + "." + found;

            error = palette + " has no color " + KebabToPascal(member);
            return null;
        }

        // a name of Colors: red, white, dark-gray
        if (compilation.GetTypeByMetadataName(ColorsMetadataName) is { } colors &&
            StaticMember(colors, value) is { } named)
        {
            return "global::" + ColorsMetadataName + "." + named;
        }

        error = "it is not a color: #rrggbb, rgb(r, g, b), a name of Colors or MediaColors.Name";
        return null;
    }

    private static bool TryByte(string text, out byte value) =>
        byte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static bool TryParseHex(string hex, out byte a, out byte r, out byte g, out byte b)
    {
        a = 255; r = g = b = 0;

        if (hex.Length is 3 or 4)
        {
            var expanded = new StringBuilder();
            foreach (char c in hex) expanded.Append(c).Append(c);
            hex = expanded.ToString();
        }

        if (hex.Length is not (6 or 8) ||
            !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value))
        {
            return false;
        }

        if (hex.Length == 6)
        {
            r = (byte)(value >> 16);
            g = (byte)(value >> 8);
            b = (byte)value;
        }
        else
        {
            // #rrggbbaa, as CSS and the style sheets write it
            r = (byte)(value >> 24);
            g = (byte)(value >> 16);
            b = (byte)(value >> 8);
            a = (byte)value;
        }

        return true;
    }

    // ===== structs and classes =====

    /// <summary>A static member of the type by a name, or a constructor by its arguments.</summary>
    private static string? WriteComposite(string value, ITypeSymbol type, Compilation compilation, out string? error)
    {
        error = null;
        string typeName = type.ToDisplayString(TypeFormat);

        List<string> tokens = Tokens(value);

        if (tokens.Count == 0)
        {
            error = "a value is missing";
            return null;
        }

        // a static member: BoxShadow.Medium, Thickness.Zero
        if (tokens.Count == 1 && IsIdentifier(tokens[0]) && StaticMember(type, tokens[0]) is { } member)
            return typeName + "." + member;

        if (type is not INamedTypeSymbol named || named.IsAbstract || named.TypeKind == TypeKind.Interface)
        {
            error = "markup writes " + type.Name + " only as one of its static members";
            return null;
        }

        IEnumerable<IMethodSymbol> constructors = named.InstanceConstructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public && Fits(c, tokens.Count))
            .OrderBy(c => c.Parameters.Length == tokens.Count ? 0 : 1)
            .ThenBy(c => c.Parameters.Length);

        string? firstError = null;

        foreach (IMethodSymbol constructor in constructors)
        {
            var arguments = new List<string>();
            bool ok = true;

            for (int i = 0; i < tokens.Count; i++)
            {
                IParameterSymbol parameter = constructor.Parameters[i];
                string? argument = Write(tokens[i], parameter.Type, compilation, out string? argumentError);

                if (argument is null)
                {
                    firstError ??= parameter.Name + ": " + argumentError;
                    ok = false;
                    break;
                }

                arguments.Add(argument);
            }

            if (ok)
                return "new " + typeName + "(" + string.Join(", ", arguments) + ")";
        }

        var arities = named.InstanceConstructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length > 0)
            .Select(c => c.Parameters.Length)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        error = firstError ?? (arities.Count == 0
            ? "markup writes " + type.Name + " only as one of its static members"
            : type.Name + " takes " + string.Join(" or ", arities) + " values, not " + tokens.Count);

        return null;
    }

    private static bool Fits(IMethodSymbol constructor, int count) =>
        constructor.Parameters.Length >= count &&
        constructor.Parameters.Skip(count).All(p => p.IsOptional) &&
        count > 0;

    /// <summary>The arguments: separated by spaces or commas outside parentheses.</summary>
    private static List<string> Tokens(string value)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        int depth = 0;

        foreach (char c in value)
        {
            if (c == '(') depth++;
            if (c == ')') depth--;

            if (depth == 0 && (c == ' ' || c == ','))
            {
                if (current.Length > 0) tokens.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0) tokens.Add(current.ToString());

        return tokens;
    }

    /// <summary>A public static field or property of the type's own type, by a name
    /// in any case, kebab or Pascal.</summary>
    private static string? StaticMember(ITypeSymbol owner, string name)
    {
        string wanted = KebabToPascal(name);

        foreach (ISymbol member in owner.GetMembers())
        {
            if (!member.IsStatic || member.DeclaredAccessibility != Accessibility.Public) continue;
            if (!string.Equals(member.Name, wanted, StringComparison.OrdinalIgnoreCase)) continue;

            if (member is IFieldSymbol or IPropertySymbol)
                return member.Name;
        }

        return null;
    }

    private static bool IsType(ITypeSymbol type, string metadataName) =>
        type.ToDisplayString() == metadataName;

    private static bool IsIdentifier(string text) =>
        text.Length > 0 && (char.IsLetter(text[0]) || text[0] == '_') &&
        text.All(c => char.IsLetterOrDigit(c) || c is '_' or '-');

    /// <summary>"dark-gray" → "DarkGray", as the style sheets read names.</summary>
    public static string KebabToPascal(string name)
    {
        if (name.IndexOf('-') < 0) return name;

        var result = new StringBuilder(name.Length);
        bool upper = true;

        foreach (char c in name)
        {
            if (c == '-')
            {
                upper = true;
                continue;
            }

            result.Append(upper ? char.ToUpperInvariant(c) : c);
            upper = false;
        }

        return result.ToString();
    }
}
