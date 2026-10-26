using ZeppelinForms.Drawing;
using ZeppelinForms.Gles.Text.OpenType;

namespace ZeppelinForms.Gles.Text.Fonts;

/// <summary>
/// A <see cref="Font"/> into the typeface it is drawn with, and a character the typeface
/// lacks into another one that has it — the way Skia's font manager does it on each system.
/// </summary>
/// <remarks>
/// <para>
/// The families of a font are tried in order — "Segoe UI, sans-serif" — and a named family
/// counts only if the face found is really of it: a source returns a substitute for a
/// family it doesn't have, and the next name in the list is the author's choice of one.
/// The generic names are the desktop's: fontconfig's on Linux, a list for each system
/// elsewhere.
/// </para>
/// <para>
/// A weight or a slant the family lacks is synthesized: a bold of a family with only a
/// regular face is the regular one widened, an italic is it slanted. A font from a file
/// is synthesized whenever asked for bold or italic, as Skia's backend does.
/// </para>
/// <para>
/// For a missing character the application's fallback files come first
/// (<see cref="FontFallbacks"/>), then the system: fontconfig asked for the character, or
/// the catalog walked — the emoji fonts first for emoji, the usual families next.
/// </para>
/// </remarks>
internal static class FontResolver
{
    private const int FallbackCacheLimit = 4096;

    private static readonly Dictionary<(string Family, FontWeight Weight, FontStyle Style, string? File), Typeface?> Primary = [];
    private static readonly Dictionary<(string Family, FontWeight Weight, FontStyle Style, int Codepoint), Typeface?> Fallbacks = [];
    private static readonly Lock Sync = new();

    private static IReadOnlyList<string>? s_fallbackPaths;
    private static List<FontFace> s_fallbackFaces = [];

    /// <summary>The source of system fonts: fontconfig where it is, the catalog elsewhere.</summary>
    public static bool UsesFontconfig => Fontconfig.IsAvailable;

    /// <summary>The typeface a font is drawn with; null only on a machine with no font at all.</summary>
    public static Typeface? Resolve(Font font)
    {
        ArgumentNullException.ThrowIfNull(font);

        var key = (font.Family, font.Weight, font.Style, font.FilePath);

        lock (Sync)
        {
            if (Primary.TryGetValue(key, out Typeface? cached)) return cached;
        }

        Typeface? resolved = ResolveCore(font);

        lock (Sync)
        {
            Primary[key] = resolved;
        }

        return resolved;
    }

    /// <summary>Forget every choice made: a font file was installed, the fallbacks changed.</summary>
    public static void Invalidate()
    {
        lock (Sync)
        {
            Primary.Clear();
            Fallbacks.Clear();
            s_fallbackPaths = null;
        }
    }

    private static Typeface? ResolveCore(Font font)
    {
        int weight = (int)font.Weight;
        bool italic = font.Style == FontStyle.Italic;

        if (font.FilePath is { } path)
        {
            try
            {
                if (FontFile.Load(path).Face(0) is { } fromFile)
                    return Typeface.Get(fromFile, embolden: weight >= 600, oblique: italic);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                // a file that can't be read leaves the family to decide, as Skia does
            }
        }

        foreach (string raw in font.Family.Split(','))
        {
            string name = raw.Trim().Trim('"', '\'');
            if (name.Length == 0) continue;

            FontFace? face = IsGeneric(name)
                ? MatchGeneric(name, weight, italic)
                : MatchFamily(name, weight, italic);

            if (face is not null) return Synthesized(face, weight, italic);
        }

        FontFace? fallback = MatchGeneric("sans-serif", weight, italic) ?? AnyFace();

        return fallback is null ? null : Synthesized(fallback, weight, italic);
    }

    /// <summary>The face with the bold and the italic it lacks made up.</summary>
    private static Typeface Synthesized(FontFace face, int weight, bool italic) =>
        Typeface.Get(face, embolden: weight >= 600 && face.Weight <= weight - 200, oblique: italic && !face.IsItalic);

    private static bool IsGeneric(string name) =>
        name.Equals("sans-serif", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("serif", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("monospace", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("system-ui", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("emoji", StringComparison.OrdinalIgnoreCase);

    private static FontFace? MatchFamily(string family, int weight, bool italic)
    {
        if (Fontconfig.IsAvailable)
        {
            // fontconfig answers every name with something: only the family asked for counts
            if (Fontconfig.Match(family, weight, italic) is { } match && Open(match.Path, match.Index) is { } face &&
                (match.Family.Equals(family, StringComparison.OrdinalIgnoreCase) ||
                 face.FamilyNames.Contains(family, StringComparer.OrdinalIgnoreCase)))
            {
                return face;
            }

            return null;
        }

        return FontCatalog.Best(FontCatalog.Family(family), weight, italic) is { } entry
            ? Open(entry.Path, entry.Index)
            : null;
    }

    private static FontFace? MatchGeneric(string generic, int weight, bool italic)
    {
        string name = generic.Equals("system-ui", StringComparison.OrdinalIgnoreCase) ? "sans-serif" : generic.ToLowerInvariant();

        if (Fontconfig.IsAvailable && Fontconfig.Match(name, weight, italic) is { } match && Open(match.Path, match.Index) is { } face)
            return face;

        foreach (string family in GenericFamilies(name))
        {
            if (FontCatalog.Best(FontCatalog.Family(family), weight, italic) is { } entry && Open(entry.Path, entry.Index) is { } found)
                return found;
        }

        return null;
    }

    /// <summary>The families a generic name means on each system, the usual first.</summary>
    internal static IReadOnlyList<string> GenericFamilies(string generic)
    {
        if (OperatingSystem.IsWindows())
        {
            return generic switch
            {
                "serif" => ["Times New Roman", "Georgia", "Cambria"],
                "monospace" => ["Consolas", "Cascadia Mono", "Courier New"],
                "emoji" => ["Segoe UI Emoji", "Segoe UI Symbol"],
                _ => ["Segoe UI", "Arial", "Tahoma"],
            };
        }

        if (OperatingSystem.IsMacOS())
        {
            return generic switch
            {
                "serif" => ["Times New Roman", "Times", "Georgia"],
                "monospace" => ["Menlo", "Monaco", "Courier New"],
                "emoji" => ["Apple Color Emoji"],
                _ => ["Helvetica Neue", "Helvetica", "Arial"],
            };
        }

        if (OperatingSystem.IsAndroid())
        {
            return generic switch
            {
                "serif" => ["Noto Serif", "Droid Serif"],
                "monospace" => ["Droid Sans Mono", "Cutive Mono"],
                "emoji" => ["Noto Color Emoji"],
                _ => ["Roboto", "Noto Sans", "Droid Sans"],
            };
        }

        return generic switch
        {
            "serif" => ["Noto Serif", "DejaVu Serif", "Liberation Serif", "FreeSerif"],
            "monospace" => ["Noto Sans Mono", "DejaVu Sans Mono", "Liberation Mono", "Ubuntu Mono", "FreeMono"],
            "emoji" => ["Noto Color Emoji", "Twemoji", "JoyPixels"],
            _ => ["Noto Sans", "DejaVu Sans", "Ubuntu", "Cantarell", "Liberation Sans", "Inter", "FreeSans"],
        };
    }

    private static FontFace? AnyFace()
    {
        foreach (CatalogEntry entry in FontCatalog.Entries)
            if (!entry.HasColor && Open(entry.Path, entry.Index) is { } face)
                return face;

        return null;
    }

    private static FontFace? Open(string path, int index)
    {
        try
        {
            return FontFile.Load(path).Face(index);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    #region Fallback

    /// <summary>A typeface that has a glyph for the character; null — none has it, and the
    /// primary one draws its missing-glyph box.</summary>
    public static Typeface? Fallback(Font font, int codepoint)
    {
        var key = (font.Family, font.Weight, font.Style, codepoint);

        lock (Sync)
        {
            RefreshApplicationFallbacks();

            if (Fallbacks.TryGetValue(key, out Typeface? cached)) return cached;
        }

        int weight = (int)font.Weight;
        bool italic = font.Style == FontStyle.Italic;

        FontFace? face = FindInApplicationFallbacks(codepoint) ?? FindInSystem(font, codepoint, weight, italic);
        Typeface? found = face is null ? null : Synthesized(face, weight, italic);

        lock (Sync)
        {
            // a pass over a page of CJK or emoji makes an entry a character: bounded
            if (Fallbacks.Count >= FallbackCacheLimit) Fallbacks.Clear();

            Fallbacks[key] = found;
        }

        return found;
    }

    /// <summary>Load the application's fallback files again when the list changed: a
    /// reference comparison, the list is replaced whole on every change.</summary>
    private static void RefreshApplicationFallbacks()
    {
        IReadOnlyList<string> paths = FontFallbacks.Paths;

        if (ReferenceEquals(paths, s_fallbackPaths)) return;

        s_fallbackPaths = paths;
        s_fallbackFaces = [];

        foreach (string path in paths)
            if (Open(path, 0) is { } face)
                s_fallbackFaces.Add(face);

        Fallbacks.Clear();
    }

    private static FontFace? FindInApplicationFallbacks(int codepoint)
    {
        List<FontFace> faces;

        lock (Sync) faces = s_fallbackFaces;

        foreach (FontFace face in faces)
            if (face.GlyphFor(codepoint) != 0)
                return face;

        return null;
    }

    private static FontFace? FindInSystem(Font font, int codepoint, int weight, bool italic)
    {
        if (Fontconfig.IsAvailable)
        {
            string first = font.Family.Split(',')[0].Trim();
            string family = IsEmoji(codepoint) ? "emoji" : first.Length > 0 ? first : "sans-serif";

            if (Fontconfig.Match(family, weight, italic, codepoint) is { } match &&
                Open(match.Path, match.Index) is { } face && face.GlyphFor(codepoint) != 0)
            {
                return face;
            }

            // fontconfig weighed every font's characters: if its best lacks it, none has it
            return null;
        }

        foreach (string family in FallbackOrder(codepoint))
        {
            if (FontCatalog.Best(FontCatalog.Family(family), weight, italic) is { } entry &&
                FontCatalog.Covers(entry, codepoint) &&
                Open(entry.Path, entry.Index) is { } face && face.GlyphFor(codepoint) != 0)
            {
                return face;
            }
        }

        return null;
    }

    /// <summary>The families to look in: the emoji fonts first for an emoji, the usual
    /// families next, every other family after.</summary>
    private static IEnumerable<string> FallbackOrder(int codepoint)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        IEnumerable<string> preferred = IsEmoji(codepoint)
            ? GenericFamilies("emoji").Concat(FontCatalog.Entries.Where(e => e.HasColor).Select(e => e.Family))
            : [];

        preferred = preferred
            .Concat(GenericFamilies("sans-serif"))
            .Concat(FontCatalog.Entries.Where(e => e.Family.StartsWith("Noto", StringComparison.OrdinalIgnoreCase)).Select(e => e.Family))
            .Concat(FontCatalog.Entries.Select(e => e.Family));

        foreach (string family in preferred)
            if (seen.Add(family))
                yield return family;
    }

    /// <summary>A character an emoji font is looked for first: pictographs, dingbats, the
    /// joiner and the presentation selectors of emoji sequences.</summary>
    internal static bool IsEmoji(int codepoint) =>
        codepoint is >= 0x1F000 and <= 0x1FAFF
        or >= 0x2600 and <= 0x27BF
        or >= 0x2B00 and <= 0x2BFF
        or 0x200D or 0x20E3 or 0xFE0F;

    #endregion
}