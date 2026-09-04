using System.Diagnostics;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Helpers;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms.Controls;

public class Button : ButtonBase, ITextElement
{
    public string? Text { get; set; }
    public HorizontalContentAlignment HorizontalContentAlign { get; set; }
    public VerticalContentAlignment VerticalContentAlign { get; set; }

    public HorizontalContentAlignment ContentAlign { get; set; } = HorizontalContentAlignment.Center;
    public VerticalContentAlignment ContentVerticalAlign { get; set; } = VerticalContentAlignment.Center;

    /// <summary>Иконка слева от текста — path data одиночного SVG-контура.</summary>
    public string? IconPathData { get; set; }
    public float IconSize { get; set; } = 16f;
    public float IconGap { get; set; } = 8f;

    protected override void DrawButtonContent(Graphics g)
    {
        Rectangle content = ContentBounds;
        float textLeft = content.X;

        if (!string.IsNullOrEmpty(IconPathData))
        {
            var icon = new Rectangle(
                new Point(content.X, content.Y + (content.Height - IconSize) / 2f),
                new Size(IconSize, IconSize));

            g.DrawSvgPath(IconPathData, icon, CurrentTextColor);
            textLeft += IconSize + IconGap;
        }

        if (string.IsNullOrEmpty(Text)) return;

        var textRect = new Rectangle(
            new Point(textLeft, content.Y),
            new Size(Math.Max(0, content.X + content.Width - textLeft), content.Height));

        g.DrawText(Text, textRect, CurrentTextColor, EffectiveFont, ContentAlign, ContentVerticalAlign);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Size textSize = string.IsNullOrEmpty(Text)
            ? Size.Empty
            : TextMeasurer.Current.MeasureText(Text, EffectiveFont);

        float width = textSize.Width + Padding.Horizontal;
        float height = Math.Max(textSize.Height, string.IsNullOrEmpty(IconPathData) ? 0 : IconSize) + Padding.Vertical;

        if (!string.IsNullOrEmpty(IconPathData))
            width += IconSize + (textSize.Width > 0 ? IconGap : 0);

        return ResolveSize(new Size(width, height), availableSize);
    }
}

public class PrimaryButton : Button
{

}

public class SecondaryButton : Button
{

}

public class DangerButton : Button 
{ 

}

public static class Buttons
{
    public static PrimaryButton Primary(string caption) => new() { Text = caption };
    public static SecondaryButton Secondary(string caption) => new() { Text = caption };
    public static DangerButton Danger(string caption) => new() { Text = caption };
}
