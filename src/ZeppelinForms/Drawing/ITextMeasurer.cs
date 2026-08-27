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

    public abstract void DrawText(string text, Point position, Color color, Font font);
    public abstract void DrawText(
        string text, Rectangle rect, Color color, Font font,
        HorizontalAlign hAlign = HorizontalAlign.Center,
        VerticalAlign vAlign = VerticalAlign.Center);
}