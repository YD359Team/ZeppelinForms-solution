using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Core.Text;
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


    /// <summary>A line laid out as it is drawn: the runs of its directions in visual
    /// order and the box of every character, for carets, selections and clicks.</summary>
    /// <param name="text">One line: no line breaks.</param>
    /// <param name="font">The font.</param>
    /// <param name="direction">The direction of the line's paragraph — the element's
    /// flow direction.</param>
    /// <remarks>
    /// The default implementation, for a measurer written before 0.15, resolves the
    /// directions itself (the Unicode Bidirectional Algorithm) and measures each run by
    /// its prefixes with <see cref="MeasureTextWidth"/>: right for a font without
    /// ligatures or contextual forms. A shaping measurer gives the boxes from its clusters.
    /// </remarks>
    TextLineLayout LayOutLine(string text, Font font, FlowDirection direction)
    {
        ArgumentNullException.ThrowIfNull(text);

        int length = text.Length;

        if (length == 0) return TextLineLayout.Empty(direction == FlowDirection.RightToLeft);

        BidiParagraph paragraph = BidiParagraph.Analyze(text, direction);

        var left = new float[length];
        var right = new float[length];
        var runs = new List<TextLineRun>();
        float x = 0;

        foreach (BidiRun run in paragraph.LineRuns(0, length))
        {
            string piece = text.Substring(run.Start, run.Length);
            float width = MeasureTextWidth(piece, piece.Length, font);
            float before = 0;

            for (int k = 0; k < piece.Length; k++)
            {
                float after = MeasureTextWidth(piece, k + 1, font);
                int index = run.Start + k;

                if (run.IsRightToLeft)
                {
                    left[index] = x + width - after;
                    right[index] = x + width - before;
                }
                else
                {
                    left[index] = x + before;
                    right[index] = x + after;
                }

                before = after;
            }

            runs.Add(new TextLineRun(run.Start, run.Length, run.IsRightToLeft, x, width));
            x += width;
        }

        return new TextLineLayout(text, paragraph.IsRightToLeft, runs, left, right);
    }

    /// <summary>Whether the font is ready for measuring. Where fonts load
    /// asynchronously, the fallback's metrics are returned until it is ready.</summary>
    bool IsReady(Font font);

    /// <summary>Load the font. On desktop platforms it completes immediately.</summary>
    Task PrepareAsync(Font font);
}