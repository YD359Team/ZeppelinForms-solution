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


    /// <summary>Whether the font is ready for measuring. Where fonts load
    /// asynchronously, the fallback's metrics are returned until it is ready.</summary>
    bool IsReady(Font font);

    /// <summary>Load the font. On desktop platforms it completes immediately.</summary>
    Task PrepareAsync(Font font);
}