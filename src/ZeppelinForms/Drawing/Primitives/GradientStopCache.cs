namespace ZeppelinForms.Drawing.Primitives;

/// <summary>
/// The stops of a gradient as the array <see cref="Graphics.FillGradient"/> takes, made
/// again only when they change: a control with a gradient used to copy its stops into a
/// new array on every frame it drew.
/// </summary>
/// <remarks>
/// An array is passed as it is. Any other list is copied once and compared with the copy
/// on the next draw — a few stops, compared in place — so a list edited where it stands,
/// a <see cref="List{T}"/> of a <c>GradientBorder</c>, is drawn as it is now too.
/// </remarks>
internal struct GradientStopCache
{
    private GradientStop[]? _stops;

    public GradientStop[] For(IReadOnlyList<GradientStop> source)
    {
        if (source is GradientStop[] array) return array;

        if (_stops is null || !SameAs(_stops, source))
            _stops = [.. source];

        return _stops;
    }

    private static bool SameAs(GradientStop[] copy, IReadOnlyList<GradientStop> source)
    {
        if (copy.Length != source.Count) return false;

        for (int i = 0; i < copy.Length; i++)
        {
            if (copy[i] != source[i]) return false;
        }

        return true;
    }
}