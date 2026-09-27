namespace ZeppelinForms.Skia;

/// <summary>
/// Counters of internal caches and pools. Needed by benchmarks and tests: retained
/// memory can't tell the constant working set of a bounded cache from real growth,
/// while the number of entries answers that directly.
/// </summary>
/// <remarks>
/// Some counters are per-thread — exactly where the cache itself is per-thread.
/// The value belongs to the thread that asks, and comparing it with a value
/// from another thread is meaningless.
/// </remarks>
public static class SkiaDiagnostics
{
    /// <summary>Entries in the font fallback cache (both generations).
    /// Bounded: the key includes the codepoint, and without a ceiling the dictionary
    /// would grow for every character met.</summary>
    public static int FallbackEntries => SkiaFontCache.FallbackCount;

    /// <summary>Entries in the current thread's cache of parsed lines.</summary>
    public static int LineEntries => SkiaFontCache.LineCount;

    /// <summary>Entries in the current thread's cache of ready SKFonts.
    /// Bounded: Size is part of Font's equality, so animating the font size
    /// creates a font for every intermediate value.</summary>
    public static int FontEntries => SkiaFontCache.FontCount;

    /// <summary>Entries in the cache of fallback fonts tied to a size.</summary>
    public static int SizedFontEntries => SkiaFontCache.SizedFontCount;

    /// <summary>Resolved typefaces. No ceiling: there are a handful of them
    /// per application.</summary>
    public static int TypefaceEntries => SkiaFontCache.TypefaceCount;

    /// <summary>How many SKPaints were created on this thread. The pool holds two
    /// brushes — for fill and for stroke — so the working value is 0, 1 or 2.
    /// More means the pool isn't working.</summary>
    public static int PaintsCreated => SkiaGraphics.PaintsCreated;

    /// <summary>How many images were uploaded into Skia over the process's lifetime.
    /// Not the cache size: ConditionalWeakTable gives no Count. Useful in comparison
    /// with the number of Images created — if it grows faster, the cache doesn't hit.</summary>
    public static int ImagesUploaded => SkiaGraphics.ImagesUploaded;
}