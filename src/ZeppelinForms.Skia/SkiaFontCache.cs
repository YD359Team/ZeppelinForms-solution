using SkiaSharp;
using System.Globalization;
using System.Text;
using ZeppelinForms.Drawing;

namespace ZeppelinForms.Skia;

internal static class SkiaFontCache
{
    // Process-wide: SKTypeface is thread-safe in Skia, there is no point
    // keeping an instance of it per thread.
    private static readonly Dictionary<string, SKTypeface> FileTypefaces = [];
    private static readonly Dictionary<(string, FontWeight, FontStyle), SKTypeface> Typefaces = [];
    private static readonly Lock Sync = new();

    /// <summary>
    /// Fallbacks by codepoint. Unlike the other dictionaries, a ceiling is needed
    /// here: the key includes the character itself, so a pass over emoji or CJK
    /// creates an entry for every character met, and the dictionary grew without
    /// bound — exactly what the memory.font-fallback-growth scenario catches.
    ///
    /// Eviction without destruction: MatchCharacter returns the same font for whole
    /// ranges, and destroying it under one key would corrupt the rest. An evicted
    /// object is released by the finalizer once no references to it remain.
    /// </summary>
    private static readonly GenerationalCache<(string, FontWeight, FontStyle, int), FallbackResult> Fallbacks =
        new(1024, disposeEvicted: false);

    internal static int FallbackCount => Fallbacks.Count;

    /// <summary>Resolved typefaces, from files and from the system.
    /// There is deliberately no ceiling here: there are a handful of them per
    /// application, and the key is a family with a style, not a character.</summary>
    internal static int TypefaceCount
    {
        get { lock (Sync) return FileTypefaces.Count + Typefaces.Count; }
    }

    // SKFont, unlike SKTypeface, is not thread-safe: MeasureText and
    // ContainsGlyphs change its internal state. So everything that contains
    // an SKFont lives as an instance per thread — for the same reason the brush
    // pool in SkiaGraphics is [ThreadStatic]: xunit snapshot tests run in parallel.
    //
    // A shared lock instead would cost an acquisition on every MeasurePrefix
    // call, and it is called for every caret position.

    [ThreadStatic] private static GenerationalCache<Font, SKFont>? _fonts;
    [ThreadStatic] private static GenerationalCache<(SKTypeface, float), SKFont>? _sizedFonts;
    [ThreadStatic] private static GenerationalCache<(string Text, Font Font), CachedLine>? _lines;

    /// <summary>The font selection version seen by this thread.</summary>
    [ThreadStatic] private static int _localVersion;

    /// <summary>The shared font selection version. Grows on every reset;
    /// per-thread caches catch up with it lazily, on first access.</summary>
    private static int _version;

    /// <summary>
    /// Ready SKFonts. The ceiling is needed because of the size in the key: Font is
    /// a record, and Size is part of its equality, so animating the font size or
    /// zooming the interface creates a separate font for every intermediate value.
    ///
    /// Eviction without destruction: FontRuns inside parsed lines refer to SKFonts,
    /// and those lines live their own life in their own cache — a font destroyed on
    /// eviction would surface when such a line is drawn.
    /// </summary>
    private static GenerationalCache<Font, SKFont> Fonts
    {
        get
        {
            SyncVersion();
            return _fonts ??= new GenerationalCache<Font, SKFont>(256, disposeEvicted: false);
        }
    }

    /// <summary>Fallback fonts tied to a size. The ceiling and the destruction rule
    /// are the same as for <see cref="Fonts"/>, and for the same reason.</summary>
    private static GenerationalCache<(SKTypeface, float), SKFont> SizedFonts
    {
        get
        {
            SyncVersion();
            return _sizedFonts ??= new GenerationalCache<(SKTypeface, float), SKFont>(256, disposeEvicted: false);
        }
    }

    /// <summary>
    /// Parsed lines. The generation limit is chosen for an interface: there are rarely
    /// more than a few hundred distinct lines on screen at once, and everything beyond
    /// that is input into a field, which goes stale by itself.
    /// </summary>
    private static GenerationalCache<(string Text, Font Font), CachedLine> Lines
    {
        get
        {
            SyncVersion();

            // blobs are native objects, eviction must release them
            return _lines ??= new GenerationalCache<(string Text, Font Font), CachedLine>(
                4096, disposeEvicted: true);
        }
    }

    internal static int LineCount => _lines?.Count ?? 0;
    internal static int FontCount => _fonts?.Count ?? 0;
    internal static int SizedFontCount => _sizedFonts?.Count ?? 0;

    /// <summary>Catch up with the shared version: if the font selection changed,
    /// the per-thread caches hold stale SKFonts and lines parsed with them.</summary>
    private static void SyncVersion()
    {
        int current = Volatile.Read(ref _version);
        if (_localVersion == current) return;

        DropLocal();
        _localVersion = current;
    }

    /// <summary>Release the per-thread caches. The order matters: first the lines
    /// together with their blobs, then the fonts themselves — lines refer to fonts
    /// through FontRun, and the reverse order would leave dangling references.</summary>
    private static void DropLocal()
    {
        _lines?.Clear();

        // no references to the fonts remain after the lines are cleared,
        // so they are destroyed here, although not on eviction
        _fonts?.Clear(disposeValues: true);
        _sizedFonts?.Clear(disposeValues: true);
    }

    public static SKFont Get(Font font)
    {
        GenerationalCache<Font, SKFont> fonts = Fonts;

        if (fonts.TryGet(font, out SKFont? cached))
            return cached!;

        SKTypeface typeface = ResolveTypeface(font);
        var skFont = new SKFont(typeface, font.Size);

        // A font file carries exactly one style, and SKTypeface.FromFile can't
        // pick by weight and slant. Bold and Italic used to be simply ignored when
        // FilePath was set — bold text was drawn regular. So they are synthesized:
        // the cache key includes Weight and Style, so the styles end up in different
        // SKFonts over one typeface.
        if (font.FilePath is not null)
        {
            if (font.Weight == FontWeight.Bold)
                skFont.Embolden = true;

            if (font.Style == FontStyle.Italic)
                skFont.SkewX = -0.25f;
        }

        fonts.Add(font, skFont);
        return skFont;
    }

    /// <summary>
    /// Reset the font selection entirely. Needed where it changes —
    /// for example, after a new font file is loaded.
    /// </summary>
    /// <remarks>
    /// Typefaces are deliberately not destroyed. Among them lies SKTypeface.Default,
    /// shared by the process, and MatchFamily can return a substitute, so one object
    /// ends up under several keys at once. There are a handful of them per reset,
    /// and the cost of a mistake here is out of proportion to the gain.
    ///
    /// May be called only when drawing is not in progress: per-thread SKFonts are
    /// released right away, and CachedLines already obtained by the caller may refer
    /// to them.
    /// </remarks>
    public static void Invalidate()
    {
        lock (Sync)
        {
            FileTypefaces.Clear();
            Typefaces.Clear();
        }

        Fallbacks.Clear();

        Interlocked.Increment(ref _version);

        // the current thread is cleaned right away, the others — on first access
        SyncVersion();
    }

    /// <summary>Reset the parsed lines. Needed where the font selection changes —
    /// for example, after a new font file is loaded.</summary>
    /// <remarks>Kept for the sake of calling code. Resetting only the lines is not
    /// enough: SKFonts and typefaces would survive the reset, and the selection would
    /// stay the same. So it does a full reset.</remarks>
    public static void InvalidateLines() => Invalidate();

    /// <summary>
    /// A line split into pieces together with its extents. The result is cached:
    /// both layout and drawing ask about the same line many times per frame,
    /// and splitting costs a pass over all characters.
    /// </summary>
    public static CachedLine GetLine(string text, Font font)
    {
        if (string.IsNullOrEmpty(text))
            return CachedLine.Empty;

        GenerationalCache<(string Text, Font Font), CachedLine> lines = Lines;

        var key = (text, font);

        if (lines.TryGet(key, out CachedLine? cached))
            return cached!;

        CachedLine line = BuildLine(text, font);
        lines.Add(key, line);

        return line;
    }

    private static CachedLine BuildLine(string text, Font font)
    {
        SKFont primary = Get(font);

        // The fast path: the whole line is covered by the primary font. One call per
        // line instead of a glyph check per character — for Latin and Cyrillic this
        // is always a hit.
        bool wholeLineIsPrimary = primary.ContainsGlyphs(text);

        FontRun[] runs = wholeLineIsPrimary
            ? [new FontRun(0, text.Length, primary, 0)]
            : BuildMixedRuns(text, font);

        float width = 0;
        float height = 0;
        SKRect bounds = SKRect.Empty;

        for (int i = 0; i < runs.Length; i++)
        {
            FontRun run = runs[i];
            ReadOnlySpan<char> span = text.AsSpan(run.Start, run.Length);

            float advance = run.Font.MeasureText(span, out SKRect runBounds);

            runs[i] = new FontRun(run.Start, run.Length, run.Font, advance);

            width += advance;

            // the height is the maximum ink height, exactly as it was computed before
            height = Math.Max(height, runBounds.Height);

            // on the fast path the only piece is the whole line measured with the
            // primary font. A separate measurement below would give the same
            // rectangle, so the one already computed is taken
            if (wholeLineIsPrimary)
                bounds = runBounds;
        }

        // on the mixed path the bounds are still computed with the primary font:
        // the baseline snapshots stand on this value
        if (!wholeLineIsPrimary)
            primary.MeasureText(text.AsSpan(), out bounds);

        return new CachedLine
        {
            Text = text,
            Runs = runs,
            Width = width,
            Height = height,
            Bounds = bounds,
        };
    }

    /// <summary>The slow path: the line has characters outside the primary font,
    /// so the selection goes by grapheme clusters.</summary>
    /// <remarks>
    /// The unit of selection is a cluster, not a rune. A composite emoji — a family,
    /// a flag, a skin tone modifier — is several runes glued with ZWJ or following one
    /// another; their components resolve to different typefaces, and a per-character
    /// walk tore such a sequence into pieces. Separate figures were drawn instead
    /// of one glyph.
    ///
    /// The font is asked for by the first rune of the cluster, and the cluster goes
    /// into the run whole: a joined sequence can't be torn apart in principle, even
    /// if its parts formally exist in different fonts.
    /// </remarks>
    private static FontRun[] BuildMixedRuns(string text, Font font)
    {
        float size = font.Size;

        var segments = new List<FontRun>();

        int start = 0;
        int position = 0;
        SKTypeface? currentTypeface = null;

        while (position < text.Length)
        {
            int clusterLength = StringInfo.GetNextTextElementLength(text.AsSpan(position));

            // protection from zero: otherwise the loop wouldn't advance and would hang
            if (clusterLength <= 0)
                clusterLength = 1;

            // selection by the cluster's first rune — its other parts
            // have no glyph of their own
            SKTypeface typeface = Resolve(font, FirstRune(text, position));

            if (currentTypeface is null)
            {
                currentTypeface = typeface;
            }
            else if (typeface.FamilyName != currentTypeface.FamilyName)
            {
                segments.Add(new FontRun(start, position - start, GetSized(currentTypeface, size), 0));
                start = position;
                currentTypeface = typeface;
            }

            position += clusterLength;
        }

        if (currentTypeface is not null && start < text.Length)
            segments.Add(new FontRun(start, text.Length - start, GetSized(currentTypeface, size), 0));

        return [.. segments];
    }

    /// <summary>The code point a cluster starts with.</summary>
    private static int FirstRune(string text, int index) =>
        Rune.TryGetRuneAt(text, index, out Rune rune)
            ? rune.Value
            : text[index];

    /// <summary>
    /// The width of the beginning of a line of length characters. No substring is
    /// created: whole pieces are taken from already computed widths, and only the tail
    /// piece is measured. The method is called for every caret position, so there
    /// must be no allocations in it.
    /// </summary>
    public static float MeasurePrefix(string text, int length, Font font)
    {
        CachedLine line = GetLine(text, font);

        float width = 0;

        foreach (FontRun run in line.Runs)
        {
            if (run.Start >= length)
                break;

            int available = Math.Min(run.Length, length - run.Start);

            width += available == run.Length
                ? run.Width
                : run.Font.MeasureText(text.AsSpan(run.Start, available));
        }

        return width;
    }

    private static SKTypeface ResolveTypeface(Font font)
    {
        // This method used to be called from Get under the shared lock. Now the font
        // dictionaries are per-thread and need no lock, and only the typefaces
        // remained shared — the lock moved here, to them.
        lock (Sync)
        {
            if (font.FilePath is not null)
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

                // MatchFamily may return a substitute instead of null if the family is
                // missing — so we check that it really is the requested font
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
    }

    /// <summary>A font that has a glyph for the character: first the primary one,
    /// then the system fallback. The result is cached — MatchCharacter creates a new
    /// object every time and costs noticeably.</summary>
    public static SKTypeface Resolve(Font font, int codepoint)
    {
        SKFont primary = Get(font);

        // the glyph check moved from SKTypeface to SKFont:
        // SKTypeface.ContainsGlyph is declared obsolete.
        // It is safe outside the lock: primary belongs to the current thread
        if (primary.ContainsGlyph(codepoint))
            return primary.Typeface;

        var key = (font.Family, font.Weight, font.Style, codepoint);

        // The cache has its own lock, but the shared lock is still needed here:
        // without it two threads on the same miss would call MatchCharacter twice
        // and create two fonts instead of one
        lock (Sync)
        {
            if (Fallbacks.TryGet(key, out FallbackResult? cached))
                return cached!.Typeface ?? primary.Typeface;

            SKTypeface? found = SKFontManager.Default.MatchCharacter(codepoint);

            Fallbacks.Add(key, found is null ? FallbackResult.None : new FallbackResult(found));

            return found ?? primary.Typeface;
        }
    }

    public static SKFont GetSized(SKTypeface typeface, float size)
    {
        GenerationalCache<(SKTypeface, float), SKFont> sized = SizedFonts;

        var key = (typeface, size);

        if (sized.TryGet(key, out SKFont? cached))
            return cached!;

        var created = new SKFont(typeface, size);
        sized.Add(key, created);
        return created;
    }

    /// <summary>Splits a line into pieces with the same font.</summary>
    /// <remarks>A compatibility wrapper over the parse cache: substrings are still
    /// created here, but the parse itself is taken ready. Drawing will move to indices
    /// in a separate step.</remarks>
    internal static IEnumerable<(string Text, SKFont Font)> SplitRuns(string text, Font font)
    {
        if (string.IsNullOrEmpty(text)) yield break;

        CachedLine line = GetLine(text, font);

        foreach (FontRun run in line.Runs)
        {
            // a piece covering the whole line is returned as is: a copy would be extra
            yield return run.Start == 0 && run.Length == text.Length
                ? (text, run.Font)
                : (text.Substring(run.Start, run.Length), run.Font);
        }
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