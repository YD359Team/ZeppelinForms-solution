using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Drawing;

public interface ITextMeasurer
{
    Size MeasureText(string text, Font font);

    /// <summary>The width of the first length characters. Needed for positioning
    /// the caret: widths can't be summed one character at a time because of kerning.</summary>
    float MeasureTextWidth(string text, int length, Font font);

    Size MeasureRuns(IReadOnlyList<TextRun> runs, Font baseFont);

    /// <summary>The font's line measures: its ascent and descent, the same for every
    /// text of it. A line of text is laid out by them, not by the ink of its letters.</summary>
    /// <remarks>A measurer written before 0.15 doesn't have it: the line height is then
    /// taken from <see cref="MeasureRuns"/>, which measures by the font, and split
    /// as a typical font splits it.</remarks>
    FontMetrics MeasureFont(Font font)
    {
        float height = MeasureRuns([new TextRun("Wg")], font).Height;
        return new FontMetrics(height * 0.8f, height * 0.2f);
    }


    /// <summary>Whether the font is ready for measuring. Where fonts load
    /// asynchronously, the fallback's metrics are returned until it is ready.</summary>
    bool IsReady(Font font);

    /// <summary>Load the font. On desktop platforms it completes immediately.</summary>
    Task PrepareAsync(Font font);
}