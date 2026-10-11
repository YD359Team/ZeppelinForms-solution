using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Drawing;

/// <summary>
/// The vertical measures a font gives every line of its text, whatever the letters:
/// how far its glyphs may rise above the baseline and fall below it.
/// </summary>
/// <remarks>
/// <para>
/// A line of text is laid out by these, not by the ink of the letters in it. The ink
/// of "OK" is shorter than that of "Apply" — the "p" and the "y" hang below the
/// baseline — and controls sized and centered by the ink came out of different
/// heights, their captions on different baselines: two buttons side by side did not
/// line up. Every platform lays text out by the font's measures for this reason.
/// </para>
/// <para>
/// Both are positive and in logical units, for the font's size.
/// </para>
/// </remarks>
/// <param name="Ascent">From the baseline up to the top of the tallest glyphs.</param>
/// <param name="Descent">From the baseline down to the bottom of the descenders.</param>
public readonly record struct FontMetrics(float Ascent, float Descent)
{
    /// <summary>The height of a line of the font: what a caption of one line takes.</summary>
    public float LineHeight => Ascent + Descent;

    /// <summary>The baseline of a line placed in a box of <paramref name="height"/>
    /// at <paramref name="top"/>: on top, at the bottom, or with its line box centered.</summary>
    public float Baseline(float top, float height, VerticalContentAlignment alignment) => alignment switch
    {
        VerticalContentAlignment.Top => top + Ascent,
        VerticalContentAlignment.Bottom => top + height - Descent,
        _ => top + (height - LineHeight) / 2f + Ascent,
    };
}
