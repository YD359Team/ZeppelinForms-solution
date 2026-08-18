using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Drawing.Helpers;

public static class ControlDrawing
{
    public static void DrawBorder(Graphics g, Rectangle rect, Color borderColor, float borderWidth)
    {
        g.DrawRectangle(rect, borderColor, borderWidth);
    }
}
