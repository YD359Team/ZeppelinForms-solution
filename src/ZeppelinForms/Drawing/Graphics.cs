using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Drawing;

public abstract class Graphics
{
    public abstract void DrawImage(Rectangle rect, Image image);
    public abstract void DrawRectangle(Rectangle rect, Color color, float width);
    public abstract void FillRectangle(Rectangle rect, Color color);
    public abstract void DrawText(string text, Point position, Color color);
    public abstract void DrawText(string text, Rectangle rect, Color color);
    public abstract void DrawText(
    string text, Rectangle rect, Color color,
    HorizontalAlign hAlign = HorizontalAlign.Center,
    VerticalAlign vAlign = VerticalAlign.Center);

    public abstract void Save();
    public abstract void Restore();
    public abstract void Translate(float dx, float dy);
}
