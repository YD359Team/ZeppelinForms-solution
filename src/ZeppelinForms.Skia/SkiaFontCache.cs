using SkiaSharp;
using ZeppelinForms.Drawing;

namespace ZeppelinForms.Skia;

internal static class SkiaFontCache
{
    private static readonly Dictionary<Font, SKFont> Fonts = [];
    private static readonly Dictionary<(string, FontWeight, FontStyle), SKTypeface> Typefaces = [];
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

    private static bool IsGeneric(string name) =>
        name is "sans-serif" or "serif" or "monospace";

    private static string GenericToConcrete(string generic) => generic switch
    {
        "serif" => "Times New Roman",
        "monospace" => "Consolas",
        _ => "Segoe UI",
    };
}
