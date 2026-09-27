using SkiaSharp;
using ZeppelinForms.Diagnostics;

namespace ZeppelinForms.Skia;

/// <summary>
/// A piece of a line entirely covered by one font. Stored as indices rather than
/// a substring: parsing must not produce copies — such a piece is measured and
/// drawn through a ReadOnlySpan, and a copy is not needed there.
/// </summary>
internal readonly struct FontRun(int start, int length, SKFont font, float width)
{
    public int Start { get; } = start;
    public int Length { get; } = length;
    public SKFont Font { get; } = font;

    /// <summary>The piece's advance width. Computed once during parsing,
    /// so that drawing doesn't measure the same thing again.</summary>
    public float Width { get; } = width;
}

/// <summary>A line split into pieces and its extents for a given font.</summary>
/// <remarks>
/// The instance belongs to the cache and is released when its generation is evicted.
/// A reference must not be held longer than one frame: the blobs may already be
/// destroyed by then.
/// </remarks>
internal sealed class CachedLine : IDisposable
{
    public static readonly CachedLine Empty = new()
    {
        Text = string.Empty,
        Runs = [],
        Width = 0,
        Height = 0,
        Bounds = SKRect.Empty,
    };

    /// <summary>The line the parse belongs to. It is the same instance as the one
    /// in the cache key, so no copy arises.</summary>
    public required string Text { get; init; }

    public required FontRun[] Runs { get; init; }

    /// <summary>The sum of the advance widths of all pieces.</summary>
    public required float Width { get; init; }

    /// <summary>The maximum ink height among the pieces. Computed exactly as it was
    /// computed before the cache appeared: all baseline snapshots stand on this value,
    /// it must not be changed.</summary>
    public required float Height { get; init; }

    /// <summary>The ink bounds of the whole line by the primary font. Needed by
    /// SkiaGraphics for vertical alignment by the baseline.</summary>
    public required SKRect Bounds { get; init; }

    /// <summary>Ready blobs, one per piece. Created lazily: a good share of lines
    /// are only measured and never drawn — hidden elements, service measurements
    /// of the line height.</summary>
    private SKTextBlob?[]? _blobs;

    /// <summary>Pieces for which building a blob has already been attempted.
    /// A separate flag is needed because null is a legitimate result:
    /// SKTextBlob.Create gives no glyphs for a whitespace piece, and without
    /// this flag such a piece would be rebuilt every frame.</summary>
    private bool[]? _probed;

    /// <summary>
    /// A piece's blob — a set of glyphs with positions, ready for output.
    /// SKCanvas.DrawText(string, ...) builds such a blob on every call and
    /// destroys it right away, that is, it rebuilds the glyph layout every frame.
    /// Here it is built once per line.
    /// </summary>
    /// <remarks>No lock: the instance lies in a per-thread cache and belongs to one
    /// thread entirely — together with the SKFont the blob is built from. There used
    /// to be a tolerated race here producing an extra blob; now there is simply
    /// nowhere for it to come from.</remarks>
    public SKTextBlob? GetBlob(int index)
    {
        ZfContract.Require(
            !_disposed,
            "CachedLine accessed after being evicted from the cache. " +
            "A reference to a parsed line must not be held longer than one frame: " +
            "its blobs are already destroyed.");

        if (_blobs is null)
        {
            _blobs = new SKTextBlob?[Runs.Length];
            _probed = new bool[Runs.Length];
        }

        if (_probed![index])
            return _blobs[index];

        FontRun run = Runs[index];

        SKTextBlob? blob = SKTextBlob.Create(
            Text.AsSpan(run.Start, run.Length), run.Font);

        _blobs[index] = blob;
        _probed[index] = true;

        return blob;
    }


    private bool _disposed;

    /// <summary>Release the blobs. Called by the cache on eviction: SKTextBlob is
    /// a wrapper over a native object, and waiting for the finalizer means holding
    /// native memory until the next collection.</summary>
    public void Dispose()
    {
        _disposed = true;

        if (_blobs is null) return;

        for (int i = 0; i < _blobs.Length; i++)
        {
            _blobs[i]?.Dispose();
            _blobs[i] = null;
        }

        _probed = null;
        _blobs = null;
    }
}

/// <summary>
/// The result of picking a fallback for a character: the font found or
/// a mark that there is no fallback.
/// </summary>
/// <remarks>
/// The wrapper is needed by the generational cache: it stores reference values,
/// and "not found" is as legitimate a result as a found font, and caching it is
/// mandatory. MatchCharacter is expensive precisely on misses: on success it stops
/// at the first suitable font, on failure it walks all installed ones.
/// </remarks>
internal sealed class FallbackResult(SKTypeface? typeface)
{
    /// <summary>A shared instance for "no fallback": there are many such entries
    /// in the cache, and there is no point creating an object for each.</summary>
    public static readonly FallbackResult None = new(null);

    public SKTypeface? Typeface { get; } = typeface;
}

/// <summary>
/// A cache with generations instead of LRU: when the hot dictionary outgrows
/// the limit, it becomes cold as a whole, and an empty one is started for new
/// entries. A hit in the cold dictionary moves the entry to the hot one, so what
/// is used survives a generation change, while one-off lines don't. There is no
/// usage list, so reading doesn't restructure anything and needs nothing but
/// one lock.
/// </summary>
/// <remarks>
/// Whether evicted values are destroyed is set at creation rather than derived from
/// the type. The check "does TValue implement IDisposable" doesn't fit here:
/// SKTypeface implements it, but fallbacks must not be destroyed — MatchCharacter
/// returns the same object for different codepoints, and the very first
/// destruction would corrupt live entries under other keys.
///
/// If destruction is on, a requirement holds: one value must not lie in two
/// generations at once.
/// </remarks>
internal sealed class GenerationalCache<TKey, TValue>(int limit, bool disposeEvicted)
    where TKey : notnull
    where TValue : class
{
    /// <summary>Entries in both generations. For diagnostics only: aggregate retained
    /// memory can't tell the constant footprint of the structures from real growth.</summary>
    internal int Count
    {
        get { lock (_sync) return _hot.Count + _cold.Count; }
    }

    private readonly System.Threading.Lock _sync = new();
    private readonly bool _disposeEvicted = disposeEvicted;

    // The capacity is set up front: the cache always grows up to the limit, and
    // growing from zero goes up a ladder of doublings, overshoots what the hint
    // would take, and throws the previous arrays into the garbage along the way.
    private Dictionary<TKey, TValue> _hot = new(limit);
    private Dictionary<TKey, TValue> _cold = new(limit);

    /// <summary>The maximum size of one generation. Up to two generations
    /// may be in memory in total.</summary>
    public int Limit { get; } = limit;

    public bool TryGet(TKey key, out TValue? value)
    {
        lock (_sync)
        {
            if (_hot.TryGetValue(key, out value))
                return true;

            if (!_cold.TryGetValue(key, out value))
                return false;

            // the entry survived a generation change — return it to the hot ones,
            // otherwise it would drop out at the very next rotation
            _hot[key] = value;

            // and remove it from the cold ones: one object in two generations
            // means that destroying the outgoing generation would release a value
            // the hot one still uses
            _cold.Remove(key);

            return true;
        }
    }

    public void Add(TKey key, TValue value)
    {
        lock (_sync)
        {
            if (_hot.Count >= Limit)
            {
                Release(_cold, _disposeEvicted);

                _cold = _hot;

                // with capacity, like the first generation: the new one will
                // immediately start growing to the same limit, and growing from
                // zero would cost a ladder of reallocations
                _hot = new Dictionary<TKey, TValue>(Limit);
            }

            // a replacement under the same key: the previous value can't be
            // reached from anywhere anymore, release it right away
            if (_hot.TryGetValue(key, out TValue? replaced) && !ReferenceEquals(replaced, value))
                Dispose(replaced);

            if (_cold.Remove(key, out TValue? stale) && !ReferenceEquals(stale, value))
                Dispose(stale);

            _hot[key] = value;
        }
    }

    public void Clear() => Clear(_disposeEvicted);

    /// <summary>
    /// Empty the cache, destroying the values or not regardless of the eviction setting.
    /// </summary>
    /// <remarks>
    /// The separation is needed by the font caches. On eviction they must not be
    /// destroyed: FontRuns inside parsed lines refer to SKFonts, and those lines live
    /// in their own cache as long as they like. On a full reset the lines are cleared
    /// first, no references remain — and destroying the fonts becomes both possible
    /// and necessary.
    /// </remarks>
    public void Clear(bool disposeValues)
    {
        lock (_sync)
        {
            Release(_hot, disposeValues);
            Release(_cold, disposeValues);

            _hot.Clear();
            _cold.Clear();
        }
    }

    private static void Release(Dictionary<TKey, TValue> generation, bool disposeValues)
    {
        if (!disposeValues) return;

        foreach (TValue value in generation.Values)
            (value as IDisposable)?.Dispose();
    }

    private void Dispose(TValue value)
    {
        if (_disposeEvicted)
            (value as IDisposable)?.Dispose();
    }
}