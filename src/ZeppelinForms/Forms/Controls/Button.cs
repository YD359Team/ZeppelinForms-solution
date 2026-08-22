using System.Diagnostics;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Helpers;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

public class Button : UnitControl, IInputElement, IBorderedElement
{
    public string? Text { get; set; }

    public Color BackgroundColor { get; set; } = LightThemeColors.ButtonFill;
    public Color HoverBackgroundColor { get; set; } = LightThemeColors.ButtonFill.Darken();
    public Color TextColor { get; set; } = Colors.White;

    // IBorderedElement
    public Color BorderColor { get; set; } = LightThemeColors.ButtonFill;
    public float BorderWidth { get; set; } = 1f;

    // IInputElement
    public event EventHandler<MouseClickEventArgs>? Click;
    public bool IsFocused { get; set; }
    public bool TabStop { get; set; } = true;
    public uint TabIndex { get; set; }

    public Button() => Size = new Size(75, 23);

    public override void Draw(Graphics g)
    {
        Debug.WriteLine($"Button.Draw pos:{Position} size:{Size}");

        g.FillRectangle(this.LocalBounds, IsHovered ? HoverBackgroundColor : BackgroundColor);

        if (BorderWidth > 0)
            g.DrawRectangle(this.LocalBounds, BorderColor, BorderWidth);

        if (Text is not null)
            g.DrawText(Text, this.ContentBounds, TextColor);
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
