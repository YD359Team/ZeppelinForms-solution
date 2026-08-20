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

    public static void DrawButton(Graphics g, Rectangle rect, ButtonStyle buttonStyle, bool isHovered, Color foreColor, Color bgColor, string? text)
    {
        if (buttonStyle == ButtonStyle.Primary)
        {
            g.FillRectangle(rect, isHovered ? LightThemeColors.AccentFill : foreColor);
            if (text is not null)
                g.DrawText(text, rect, bgColor);
        }
        else if (buttonStyle == ButtonStyle.Secondary)
        {
            g.FillRectangle(rect, isHovered ? LightThemeColors.AccentBackground : bgColor);
            if (text is not null)
                g.DrawText(text, rect, foreColor);
        }
        else
        {
            throw new NotImplementedException();
        }
    }
}
