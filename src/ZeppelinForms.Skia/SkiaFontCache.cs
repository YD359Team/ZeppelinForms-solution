using SkiaSharp;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using ZeppelinForms.Core.Text;
using ZeppelinForms.Drawing;

namespace ZeppelinForms.Skia;

internal static class SkiaFontCache
{
    // Process-wide: SKTypeface is thread-safe in Skia, there is no point
    // keeping an instance of it per thread.
    //
    // No global lock any more. ResolveTypeface used to hold one shared lock through
    // SKTypeface.FromFile and MatchFamily — a read from disk and a font manager query,
    // on Linux a fontconfig pass that takes long the first time — so a miss on one
    // thread held up every other thread's miss, and a parallel snapshot run waited on
    // whichever thread got there first. Now a hit takes no lock at all, misses of
    // different keys go on in parallel, and two threads on the same miss still do the
    // work once: the second waits on the first one's Lazy, not on a lock of everything.

    /// <summary>Resolved typefaces, from files and from the system. Replaced whole by
    /// <see cref="Invalidate"/>: a resolution still running for the old selection
    /// finishes into the old table, which nobody reads any more.</summary>
    private static TypefaceTable _typefaces = new();

    /// <summary>The application's fallback files as last loaded, and the fallbacks
    /// found with them. Replaced whole when <see cref="FontFallbacks"/> changes or on
    /// <see cref="Invalidate"/>; a miss computed with an older set goes into that set's
    /// cache, which nobody reads any more.</summary>
    private static FallbackSet _fallbackSet = FallbackSet.Unloaded();

    /// <summary>Taken only to load a new set of fallback files.</summary>
    private static readonly Lock FallbackLoadSync = new();

    /// <summary>
    /// Locks for fallback misses, by the key's hash. MatchCharacter creates a new object
    /// every time and costs noticeably: two threads on the same miss would make two
    /// fonts instead of one, so a miss is computed under the lock of its stripe. Misses
    /// of other characters — on other stripes — go on in parallel, and a hit takes
    /// none of them.
    /// </summary>
    private static readonly Lock[] FallbackMissLocks = [.. Enumerable.Range(0, 16).Select(_ => new Lock())];

    internal static int FallbackCount => Volatile.Read(ref _fallbackSet).Cache.Count;

    /// <summary>Resolved typefaces, from files and from the system.
    /// There is deliberately no ceiling here: there are a handful of them per
    /// application, and the key is a family with a style, not a character.</summary>
    /// <remarks>For diagnostics: a file that couldn't be loaded is not counted,
    /// as it wasn't before.</remarks>
    internal static int TypefaceCount
    {
        get
        {
            TypefaceTable table = Volatile.Read(ref _typefaces);

            return table.Families.Count +
                   table.Files.Count(entry => entry.Value.IsValueCreated && entry.Value.Value is not null);
        }
    }

    /// <summary>Typefaces by file and by family with a style. A value is a Lazy: the
    /// first thread to miss a key resolves it, the others on the same key wait for
    /// that one result.</summary>
    private sealed class TypefaceTable
    {
        /// <summary>By file path; null — the file couldn't be loaded, and the font falls
        /// back to its family. Kept as well, so the file isn't read again for every
        /// new size and on every thread.</summary>
        public ConcurrentDictionary<string, Lazy<SKTypeface?>> Files { get; } = new(StringComparer.Ordinal);

        public ConcurrentDictionary<(string Family, FontWeight Weight, FontStyle Style), Lazy<SKTypeface>> Families { get; } = new();
    }

    /// <summary>
    /// The application's fallback files, loaded, with the fallbacks found for them.
    /// </summary>
    /// <param name="paths">The list of <see cref="FontFallbacks"/> the files were loaded
    /// from; null — none loaded yet. Compared by reference: the list is replaced whole
    /// on every change.</param>
    /// <param name="typefaces">A typeface of each file that could be read.</param>
    private sealed class FallbackSet(IReadOnlyList<string>? paths, SKTypeface[] typefaces)
    {
        public IReadOnlyList<string>? Paths { get; } = paths;

        public SKTypeface[] Typefaces { get; } = typefaces;

        /// <summary>
        /// Fallbacks by codepoint. Unlike the typefaces, a ceiling is needed here: the
        /// key includes the character itself, so a pass over emoji or CJK creates an
        /// entry for every character met, and the dictionary grew without bound —
        /// exactly what the memory.font-fallback-growth scenario catches.
        ///
        /// Eviction without destruction: MatchCharacter returns the same font for whole
        /// ranges, and destroying it under one key would corrupt the rest. An evicted
        /// object is released by the finalizer once no references to it remain.
        /// </summary>
        /// <remarks>One per set: a character drawn as a box before the files were
        /// added is looked for again with the new ones.</remarks>
        public GenerationalCache<(string, FontWeight, FontStyle, int), FallbackResult> Cache { get; } =
            new(1024, disposeEvicted: false);

        /// <summary>A set to be replaced on first use: a new instance every time, so
        /// that nothing is ever cached into a shared one.</summary>
        public static FallbackSet Unloaded() => new(null, []);
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
    [ThreadStatic] private static GenerationalCache<(string Text, Font Font, bool RightToLeft), CachedLine>? _lines;

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
    /// <remarks>The direction of the paragraph is in the key: the same text is ordered
    /// differently in a right-to-left element.</remarks>
    private static GenerationalCache<(string Text, Font Font, bool RightToLeft), CachedLine> Lines
    {
        get
        {
            SyncVersion();

            // blobs are native objects, eviction must release them
            return _lines ??= new GenerationalCache<(string Text, Font Font, bool RightToLeft), CachedLine>(
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

        // HarfBuzz fonts are made from typefaces of the old selection
        SkiaShaper.Clear();
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
        //
        // Skia's embolden has a single strength, so SemiBold is synthesized the same
        // way as Bold: drawn regular, the Fluent type ramp would lose its titles —
        // they differ from body text by weight as much as by size. Light has no
        // synthesis at all and is drawn with the file's own weight.
        if (font.FilePath is not null)
        {
            if (font.Weight >= FontWeight.SemiBold)
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
        // new tables in place of the old: a resolution running on another thread
        // right now finishes into the old ones and is forgotten with them. The
        // fallback files are loaded again on the next miss — a font file just loaded
        // may well be among them
        Volatile.Write(ref _typefaces, new TypefaceTable());
        Volatile.Write(ref _fallbackSet, FallbackSet.Unloaded());

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
    public static CachedLine GetLine(string text, Font font) => GetLine(text, font, rightToLeft: false);

    /// <summary>A line in a paragraph of the given direction: its pieces in visual order.</summary>
    public static CachedLine GetLine(string text, Font font, bool rightToLeft)
    {
        if (string.IsNullOrEmpty(text))
            return CachedLine.Empty;

        GenerationalCache<(string Text, Font Font, bool RightToLeft), CachedLine> lines = Lines;

        var key = (text, font, rightToLeft);

        if (lines.TryGet(key, out CachedLine? cached))
            return cached!;

        CachedLine line = BuildLine(WithoutTabs(text), font, rightToLeft);

        lines.Add(key, line);

        return line;
    }

    /// <summary>A tab is laid out and drawn as a space.</summary>
    /// <remarks>
    /// Fonts have no glyph for U+0009: a line with a tab left the fast path, the
    /// fallback search found nothing better, and the tab was drawn as the missing
    /// glyph's box — in a text field, with whitespace marks off. The replacement keeps
    /// the length, so every index of the caller's text is the same index here, and the
    /// cache stays keyed by the text as given. The tab is one space wide; in a
    /// right-to-left line it no longer separates the bidirectional segments, which
    /// matters only for tab-aligned columns of mixed-direction text.
    /// </remarks>
    private static string WithoutTabs(string text) =>
        text.Contains('\t') ? text.Replace('\t', ' ') : text;

    /// <summary>Whether the line needs the bidirectional algorithm or the shaper. Without
    /// either it is parsed as it always was — a glyph per character, left to right.</summary>
    private static bool NeedsLayout(string text) => ComplexScripts.NeedsShaping(text);

    private static CachedLine BuildLine(string text, Font font, bool rightToLeft)
    {
        if (rightToLeft || NeedsLayout(text))
            return BuildOrderedLine(text, font, rightToLeft);

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

    /// <summary>
    /// A line that needs ordering or shaping: resolved by the Unicode Bidirectional
    /// Algorithm into runs of one direction, each split by font and shaped by HarfBuzz.
    /// The pieces go into the line in visual order, left to right.
    /// </summary>
    /// <remarks>
    /// A right-to-left run is always shaped, even of characters that need no shaping —
    /// punctuation at the end of a right-to-left paragraph: the shaper orders its glyphs
    /// and mirrors brackets. A left-to-right run is shaped when the line has anything
    /// complex: its letters may join the marks around them.
    /// </remarks>
    private static CachedLine BuildOrderedLine(string text, Font font, bool rightToLeft)
    {
        SKFont primary = Get(font);

        bool complex = ComplexScripts.NeedsShaping(text);

        BidiParagraph paragraph = BidiParagraph.Analyze(
            text, rightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight);

        var runs = new List<FontRun>();
        float width = 0;
        float height = 0;

        foreach (BidiRun bidi in paragraph.LineRuns(0, text.Length))
        {
            FontRun[] pieces = primary.ContainsGlyphs(text.AsSpan(bidi.Start, bidi.Length))
                ? [new FontRun(bidi.Start, bidi.Length, primary, 0)]
                : BuildMixedRuns(text, bidi.Start, bidi.Start + bidi.Length, font);

            // the pieces of a right-to-left run stand right to left as well
            if (bidi.IsRightToLeft)
                Array.Reverse(pieces);

            foreach (FontRun piece in pieces)
            {
                ReadOnlySpan<char> span = text.AsSpan(piece.Start, piece.Length);

                // the ink height as the plain path measures it: the baselines stand on it
                float advance = piece.Font.MeasureText(span, out SKRect pieceBounds);
                height = Math.Max(height, pieceBounds.Height);

                ShapedRun? shaped = complex || bidi.IsRightToLeft
                    ? SkiaShaper.Shape(text, piece.Start, piece.Length, piece.Font, bidi.IsRightToLeft)
                    : null;

                float pieceWidth = shaped?.Width ?? advance;

                runs.Add(new FontRun(piece.Start, piece.Length, piece.Font, pieceWidth)
                {
                    IsRightToLeft = bidi.IsRightToLeft,
                    Shaped = shaped,
                });

                width += pieceWidth;
            }
        }

        primary.MeasureText(text.AsSpan(), out SKRect bounds);

        return new CachedLine
        {
            Text = text,
            Runs = [.. runs],
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
    private static FontRun[] BuildMixedRuns(string text, Font font) => BuildMixedRuns(text, 0, text.Length, font);

    /// <summary>The same over a part of the line, from start up to end.</summary>
    private static FontRun[] BuildMixedRuns(string text, int from, int end, Font font)
    {
        float size = font.Size;

        var segments = new List<FontRun>();

        int start = from;
        int position = from;
        SKTypeface? currentTypeface = null;

        while (position < end)
        {
            int clusterLength = StringInfo.GetNextTextElementLength(text.AsSpan(position, end - position));

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

        if (currentTypeface is not null && start < end)
            segments.Add(new FontRun(start, end - start, GetSized(currentTypeface, size), 0));

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

        // the pieces are in visual order: those of the prefix may be anywhere among them
        foreach (FontRun run in line.Runs)
        {
            if (run.Start >= length)
                continue;

            int available = Math.Min(run.Length, length - run.Start);

            if (available == run.Length)
            {
                width += run.Width;
            }
            else if (run.Shaped is { } shaped)
            {
                for (int k = 0; k < available; k++)
                    width += shaped.CharacterRights[k] - shaped.CharacterLefts[k];
            }
            else
            {
                width += run.Font.MeasureText(text.AsSpan(run.Start, available));
            }
        }

        return width;
    }

    private static SKTypeface ResolveTypeface(Font font)
    {
        // This method used to be called from Get under the shared lock. Now the font
        // dictionaries are per-thread and need no lock, and the typefaces, the only
        // shared part, are a concurrent table: see _typefaces
        TypefaceTable table = Volatile.Read(ref _typefaces);

        if (font.FilePath is { } file &&
            GetOrResolve(table.Files, file, static path => SKTypeface.FromFile(path)) is { } fromFile)
            return fromFile;

        return GetOrResolve(table.Families, (font.Family, font.Weight, font.Style), MatchFamily);
    }

    /// <summary>
    /// A value of a typeface table: a hit without any lock; a miss resolved once, by the
    /// first thread to ask, while the others on the same key wait for its result.
    /// </summary>
    /// <remarks>A resolution that throws is not kept: Lazy would keep the exception and
    /// throw it for that key until the next <see cref="Invalidate"/>.</remarks>
    private static TValue GetOrResolve<TKey, TValue>(
        ConcurrentDictionary<TKey, Lazy<TValue>> table,
        TKey key,
        Func<TKey, TValue> resolve)
        where TKey : notnull
    {
        if (!table.TryGetValue(key, out Lazy<TValue>? entry))
        {
            entry = table.GetOrAdd(
                key,
                static (k, r) => new Lazy<TValue>(() => r(k), LazyThreadSafetyMode.ExecutionAndPublication),
                resolve);
        }

        try
        {
            return entry.Value;
        }
        catch
        {
            table.TryRemove(new KeyValuePair<TKey, Lazy<TValue>>(key, entry));
            throw;
        }
    }

    /// <summary>A system typeface for a family list with a weight and a slant: the first
    /// family of the list that is installed, or the default typeface.</summary>
    private static SKTypeface MatchFamily((string Family, FontWeight Weight, FontStyle Style) key)
    {
        // FontWeight holds the numeric weights of CSS and OpenType, the same
        // scale SKFontStyle takes, so the value is passed as is. It used to be
        // mapped to Bold or Normal only — a scale of two points, where Light and
        // SemiBold had no place. A family without the exact face is matched to
        // the nearest one by the font manager
        var style = new SKFontStyle(
            (int)key.Weight,
            (int)SKFontStyleWidth.Normal,
            key.Style == FontStyle.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);

        foreach (string raw in key.Family.Split(','))
        {
            string name = raw.Trim();
            if (name.Length == 0) continue;

            if (IsGeneric(name))
            {
                if (SKFontManager.Default.MatchFamily(GenericToConcrete(name), style) is { } generic)
                    return generic;

                continue;
            }

            SKTypeface? candidate = SKFontManager.Default.MatchFamily(name, style);

            // MatchFamily may return a substitute instead of null if the family is
            // missing — so we check that it really is the requested font
            if (candidate is not null &&
                candidate.FamilyName.Equals(name, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return SKTypeface.Default;
    }

    /// <summary>The fallback files of <see cref="FontFallbacks"/>: the set loaded from the
    /// current list, loaded now if the list changed since the last one. The common case —
    /// the list as it was — is a reference comparison and no lock.</summary>
    private static FallbackSet CurrentFallbacks()
    {
        IReadOnlyList<string> paths = FontFallbacks.Paths;
        FallbackSet set = Volatile.Read(ref _fallbackSet);

        if (ReferenceEquals(paths, set.Paths)) return set;

        // one thread loads the files, the others on the same change wait for it rather
        // than read the same files again
        lock (FallbackLoadSync)
        {
            set = Volatile.Read(ref _fallbackSet);

            if (ReferenceEquals(paths, set.Paths)) return set;

            var typefaces = new List<SKTypeface>(paths.Count);

            foreach (string path in paths)
            {
                // a file that can't be read is skipped rather than failing the text:
                // the system's fallback, if any, is still there
                if (SKTypeface.FromFile(path) is { } typeface)
                    typefaces.Add(typeface);
            }

            // a new set comes with an empty cache of its own: a character drawn as
            // a box before the files were added is looked for again
            var loaded = new FallbackSet(paths, [.. typefaces]);
            Volatile.Write(ref _fallbackSet, loaded);

            return loaded;
        }
    }

    /// <summary>A font that has a glyph for the character: first the primary one,
    /// then the application's fallback files (<see cref="FontFallbacks"/>), then the
    /// system fallback. The result is cached — MatchCharacter creates a new object
    /// every time and costs noticeably.</summary>
    public static SKTypeface Resolve(Font font, int codepoint)
    {
        SKFont primary = Get(font);

        // the glyph check moved from SKTypeface to SKFont:
        // SKTypeface.ContainsGlyph is declared obsolete.
        // It is safe outside the lock: primary belongs to the current thread
        if (primary.ContainsGlyph(codepoint))
            return primary.Typeface;

        // a list of fallback files changed since the last miss: a new set, with an
        // empty cache of its own
        FallbackSet set = CurrentFallbacks();

        var key = (font.Family, font.Weight, font.Style, codepoint);

        // the hit: the cache's own short lock, nothing shared besides it
        if (set.Cache.TryGet(key, out FallbackResult? cached))
            return cached!.Typeface ?? primary.Typeface;

        // The miss: under the lock of the key's stripe, so two threads on the same
        // miss call MatchCharacter once and get one font, while misses on other
        // stripes don't wait for this one
        lock (FallbackMissLocks[(key.GetHashCode() & int.MaxValue) % FallbackMissLocks.Length])
        {
            if (set.Cache.TryGet(key, out cached))
                return cached!.Typeface ?? primary.Typeface;

            // the application's files first: in a browser they are the only fallback
            SKTypeface? found = FindInApplicationFallbacks(set, codepoint) ?? SKFontManager.Default.MatchCharacter(codepoint);

            set.Cache.Add(key, found is null ? FallbackResult.None : new FallbackResult(found));

            return found ?? primary.Typeface;
        }
    }

    /// <summary>The fallback files of <see cref="FontFallbacks"/> as last loaded: the
    /// list they were loaded from, and a font of each to check glyphs with. Guarded by
    /// <see cref="Sync"/>.</summary>
    private static IReadOnlyList<string>? _fallbackPaths;
    private static List<SKFont> _fallbackFonts = [];

    /// <summary>Load the fallback files again if <see cref="FontFallbacks"/> changed since
    /// they were last loaded, and drop the cached fallbacks then: a character drawn as a
    /// box before the files were added is looked for again. A reference comparison —
    /// the list is replaced whole on every change. Called under <see cref="Sync"/>.</summary>
    private static void RefreshApplicationFallbacks()
    {
        IReadOnlyList<string> paths = FontFallbacks.Paths;

        if (ReferenceEquals(paths, _fallbackPaths)) return;

        _fallbackPaths = paths;
        _fallbackFonts = [];

        foreach (string path in paths)
        {
            // a file that can't be read is skipped rather than failing the text:
            // the system's fallback, if any, is still there
            if (SKTypeface.FromFile(path) is { } typeface)
                _fallbackFonts.Add(new SKFont(typeface));
        }

        Fallbacks.Clear();
    }

    /// <summary>The first of the application's fallback files with a glyph for the
    /// character; null — none has it. Called under <see cref="Sync"/>.</summary>
    private static SKTypeface? FindInApplicationFallbacks(int codepoint)
    {
        foreach (SKFont fallback in _fallbackFonts)
            if (fallback.ContainsGlyph(codepoint))
                return fallback.Typeface;

        return null;
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