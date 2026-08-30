using SkiaSharp;
using System.Text;
using ZeppelinForms.Drawing;

namespace ZeppelinForms.Skia;

internal static class SkiaFontCache
{
    private static readonly Dictionary<Font, SKFont> Fonts = [];
    private static readonly Dictionary<string, SKTypeface> FileTypefaces = [];
    private static readonly Dictionary<(string, FontWeight, FontStyle), SKTypeface> Typefaces = [];
    private static readonly Dictionary<(string, FontWeight, FontStyle, int), SKTypeface?> Fallbacks = [];
    private static readonly Lock Sync = new();

    public static SKFont Get(Font font)
    {
        lock (Sync)
        {
            if (Fonts.TryGetValue(font, out SKFont? cached))
                return cached;

            SKTypeface typeface = ResolveTypeface(font);
            var skFont = new SKFont(typeface, font.Size);

            Fonts[font] = skFont;
            return skFont;
        }
    }

    private static SKTypeface ResolveTypeface(Font font)
    {
        if (font.FilePath is not null)
        {
            lock (Sync)
            {
                if (FileTypefaces.TryGetValue(font.FilePath, out SKTypeface? fromFile))
                    return fromFile;

                SKTypeface? loaded = SKTypeface.FromFile(font.FilePath);

                if (loaded is not null)
                {
                    FileTypefaces[font.FilePath] = loaded;
                    return loaded;
                }
            }
        }

        var key = (font.Family, font.Weight, font.Style);

        if (Typefaces.TryGetValue(key, out SKTypeface? cached))
            return cached;

        var style = new SKFontStyle(
            font.Weight == FontWeight.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            font.Style == FontStyle.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);

        SKTypeface? resolved = null;

        foreach (string raw in font.Family.Split(','))
        {
            string name = raw.Trim();
            if (name.Length == 0) continue;

            if (IsGeneric(name))
            {
                resolved = SKFontManager.Default.MatchFamily(GenericToConcrete(name), style);
                if (resolved is not null) break;
                continue;
            }

            SKTypeface? candidate = SKFontManager.Default.MatchFamily(name, style);

            // MatchFamily может вернуть подмену вместо null, если семейства нет —
            // поэтому проверяем, что это действительно запрошенный шрифт
            if (candidate is not null &&
                candidate.FamilyName.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                resolved = candidate;
                break;
            }
        }

        resolved ??= SKTypeface.Default;
        Typefaces[key] = resolved;
        return resolved;
    }

    /// <summary>Шрифт, в котором есть глиф для символа: сначала основной,
    /// потом системная подстановка. Результат кэшируется — MatchCharacter
    /// каждый раз создаёт новый объект и заметно стоит.</summary>
    public static SKTypeface Resolve(Font font, int codepoint)
    {
        SKTypeface primary = SkiaFontCache.Get(font).Typeface;

        if (primary.ContainsGlyph(codepoint))
            return primary;

        var key = (font.Family, font.Weight, font.Style, codepoint);

        lock (Sync)
        {
            if (Fallbacks.TryGetValue(key, out SKTypeface? cached))
                return cached ?? primary;

            SKTypeface? found = SKFontManager.Default.MatchCharacter(codepoint);
            Fallbacks[key] = found;
            return found ?? primary;
        }
    }

    public static SKFont GetSized(SKTypeface typeface, float size)
    {
        var key = (typeface, size);

        lock (Sync)
        {
            if (SizedFonts.TryGetValue(key, out SKFont? cached))
                return cached;

            var created = new SKFont(typeface, size);
            SizedFonts[key] = created;
            return created;
        }
    }

    /// <summary>Разбивает строку на отрезки с одинаковым шрифтом.</summary>
    internal static IEnumerable<(string Text, SKFont Font)> SplitRuns(string text, Font font)
    {
        if (string.IsNullOrEmpty(text)) yield break;

        float size = font.Size;
        int start = 0;
        int position = 0;
        SKTypeface? currentTypeface = null;

        foreach (Rune rune in text.EnumerateRunes())
        {
            SKTypeface typeface = Resolve(font, rune.Value);

            if (currentTypeface is null)
            {
                currentTypeface = typeface;
            }
            else if (typeface.FamilyName != currentTypeface.FamilyName)
            {
                yield return (text[start..position], GetSized(currentTypeface, size));
                start = position;
                currentTypeface = typeface;
            }

            position += rune.Utf16SequenceLength;
        }

        if (start < text.Length && currentTypeface is not null)
            yield return (text[start..], GetSized(currentTypeface, size));
    }

    private static readonly Dictionary<(SKTypeface, float), SKFont> SizedFonts = [];

    private static bool IsGeneric(string name) =>
        name is "sans-serif" or "serif" or "monospace";

    private static string GenericToConcrete(string generic) => generic switch
    {
        "serif" => "Times New Roman",
        "monospace" => "Consolas",
        _ => "Segoe UI",
    };
}
