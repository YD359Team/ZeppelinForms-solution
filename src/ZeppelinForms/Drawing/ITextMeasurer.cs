using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Drawing;

public interface ITextMeasurer
{
    Size MeasureText(string text, Font font);
    float MeasureTextWidth(string text, int length, Font font);
    Size MeasureRuns(IReadOnlyList<TextRun> runs, Font baseFont);
}