using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Drawing;

/// <summary>Reading gradients by hand: for a backend that draws them from pieces.</summary>
internal static class GradientMath
{
    /// <summary>The color of the gradient at a position from 0 to 1: the nearest stops
    /// mixed, the first's color before them, the last's after. The stops in their order,
    /// as the backends take them.</summary>
    public static Color Sample(ReadOnlySpan<GradientStop> stops, float position)
    {
        if (stops.Length == 0) return Colors.Transparent;

        if (position <= stops[0].Offset) return stops[0].Color;

        for (int i = 1; i < stops.Length; i++)
        {
            GradientStop next = stops[i];

            if (position > next.Offset) continue;

            GradientStop previous = stops[i - 1];
            float span = next.Offset - previous.Offset;

            return span <= 0f
                ? next.Color
                : Color.Lerp(previous.Color, next.Color, (position - previous.Offset) / span);
        }

        return stops[^1].Color;
    }
}
