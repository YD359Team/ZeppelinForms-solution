using ZeppelinForms.Gles.Text.OpenType;

namespace ZeppelinForms.Gles.Text.Fonts;

/// <summary>
/// A face as it is drawn: the font's own face, and whether a bold or an italic the face
/// doesn't have is synthesized — the outline widened, or slanted.
/// </summary>
/// <remarks>One object for each combination, made once: glyph caches are keyed by its
/// <see cref="Id"/>, and lines compare their pieces' typefaces by reference.</remarks>
internal sealed class Typeface
{
    private static readonly Dictionary<(int Face, bool Embolden, bool Oblique), Typeface> All = [];
    private static readonly Lock AllLock = new();
    private static int s_nextId;

    private Typeface(FontFace face, bool embolden, bool oblique)
    {
        Face = face;
        Embolden = embolden;
        Oblique = oblique;
        Id = Interlocked.Increment(ref s_nextId);
    }

    public static Typeface Get(FontFace face, bool embolden, bool oblique)
    {
        lock (AllLock)
        {
            var key = (face.Id, embolden, oblique);

            if (!All.TryGetValue(key, out Typeface? typeface))
            {
                typeface = new Typeface(face, embolden, oblique);
                All[key] = typeface;
            }

            return typeface;
        }
    }

    public FontFace Face { get; }

    /// <summary>A bold the face lacks: its outline is widened by a 24th of the em, as
    /// FreeType's emboldening does for Skia.</summary>
    public bool Embolden { get; }

    /// <summary>An italic the face lacks: its outline slants by a quarter, as Skia's skew.</summary>
    public bool Oblique { get; }

    public int Id { get; }

    /// <summary>The slant of a synthesized italic: x moves right by a quarter of y.</summary>
    public const float ObliqueSkew = 0.25f;

    /// <summary>The widening of a synthesized bold, a share of the em.</summary>
    public const float EmboldenStrength = 1f / 24f;

    public float Scale(float size) => size / Face.UnitsPerEm;

    /// <summary>Up from the baseline, positive.</summary>
    public float Ascent(float size) => Face.Ascender * Scale(size);

    /// <summary>Down from the baseline, positive.</summary>
    public float Descent(float size) => -Face.Descender * Scale(size);

    public override string ToString() =>
        $"{Face}{(Embolden ? " +bold" : "")}{(Oblique ? " +oblique" : "")}";
}