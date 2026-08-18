using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Drawing;

public abstract class Graphics
{
    public abstract void DrawRectangle(Rectangle rect, Color color, float width);
    public abstract void FillRectangle(Rectangle rect, Color color);
    public abstract void DrawText(string text, Point position, Color color);

    public abstract void Save();
    public abstract void Restore();
    public abstract void Translate(float dx, float dy);
}
