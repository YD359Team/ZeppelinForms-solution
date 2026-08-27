using System.Diagnostics;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Helpers;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms.Controls;

public class Button : UnitControl, ITextElement, IInputElement, IBorderedElement
{
    public Color BackgroundColor { get; set; } = LightThemeColors.ButtonFill;
    public Color HoverBackgroundColor { get; set; } = LightThemeColors.ButtonFill.Darken();
    // ITextElement
    public string? Text { get; set; }
    public HorizontalAlign HorizontalAlign { get; set; }
    public VerticalAlign VerticalAlign { get; set; }
    public Color TextColor { get; set; } = Colors.White;
    // IBorderedElement
    public Color BorderColor { get; set; } = LightThemeColors.ButtonFill;
    public float BorderWidth { get; set; } = 1f;

    // IInputElement
    public bool IsFocused { get; set; }
    public bool TabStop { get; set; } = true;
    public uint TabIndex { get; set; }

    public override void Draw(Graphics g)
    {
        g.FillRectangle(this.LocalBounds, IsHovered ? HoverBackgroundColor : BackgroundColor);

        if (BorderWidth > 0)
            g.DrawRectangle(this.LocalBounds, BorderColor, BorderWidth);

        if (Text is not null)
            g.DrawText(this.Text, this.ContentBounds, this.TextColor, this.EffectiveFont,
                this.HorizontalAlign, this.VerticalAlign);
    }


    protected override Size MeasureOverride(Size availableSize)
    {
        Size textSize = string.IsNullOrEmpty(Text)
            ? Size.Empty
            : TextMeasurer.Current.MeasureText(Text, EffectiveFont);

        // немного запаса под рамку/визуальный "воздух" кнопки сверх чистого текста
        var content = new Size(textSize.Width + Padding.Horizontal + 16, textSize.Height + Padding.Vertical + 8);
        return ResolveSize(content, availableSize);
    }
}

public static class Buttons
{
    public static Button Primary(string text) => new()
    {
        Text = text,
        BackgroundColor = LightThemeColors.ButtonFill,
        HoverBackgroundColor = LightThemeColors.ButtonFill.Darken(),
        TextColor = Colors.White,
        BorderColor = LightThemeColors.ButtonFill,
    };

    public static Button Secondary(string text) => new()
    {
        Text = text,
        BackgroundColor = Colors.White,
        HoverBackgroundColor = LightThemeColors.AccentBackground,
        TextColor = LightThemeColors.ButtonFill,
        BorderColor = LightThemeColors.ButtonFill,
    };
}