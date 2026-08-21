using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;

namespace ZeppelinForms.Drawing.Helpers;

public static class ControlDrawing
{
    public static void DrawBorder(Graphics g, Rectangle rect, Color borderColor, float borderWidth)
    {
        g.DrawRectangle(rect, borderColor, borderWidth);
    }
}