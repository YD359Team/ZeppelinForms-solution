using System.Diagnostics;
using ZeppelinForms.Core.Globalization;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Helpers;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms.Controls;

public class Button : ButtonBase, ITextElement
{
    public string? Text
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // a button's auto-size is computed from its text
            Invalidate();
        }
    }

    public HorizontalContentAlignment HorizontalContentAlign { get; set; } = HorizontalContentAlignment.Center;
    public VerticalContentAlignment VerticalContentAlign { get; set; } = VerticalContentAlignment.Center;

    /// <summary>An icon beside the text: SVG path data in the text's color, or a picture.
    /// Where it stands — <see cref="IconPlacement"/>.</summary>
    public IconSource? Icon
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // an icon appearing or disappearing changes both the size and the gap
            Invalidate();
        }
    }

    /// <summary>An icon from path data of a single SVG contour — the same as
    /// <see cref="Icon"/> = <see cref="IconSource.FromPath"/>.</summary>
    public string? IconPathData
    {
        get => (Icon as PathIconSource)?.Data;
        set => Icon = string.IsNullOrEmpty(value) ? null : new PathIconSource(value);
    }

    /// <summary>Before or after the text, above or below it. Before and after follow
    /// the reading direction: in a right-to-left layout the start is on the right.</summary>
    public IconPlacement IconPlacement
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // beside or above: the button's shape changes
            Invalidate();
        }
    }

    public float IconSize
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Invalidate();
        }
    } = 16f;

    public float IconGap
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Invalidate();
        }
    } = 8f;

    private bool IconIsVertical => IconPlacement is IconPlacement.Top or IconPlacement.Bottom;

    /// <summary>Whether the icon comes first along the line: before the text in the
    /// reading direction, mirrored in a right-to-left layout.</summary>
    private bool IconIsLeft => (IconPlacement == IconPlacement.Start) != IsRightToLeft;

    protected override void DrawButtonContent(Graphics g)
    {
        Rectangle content = ContentBounds;
        IconSource? icon = Icon;
        string? text = string.IsNullOrEmpty(Text) ? null : ApplyTextTransform(Text);

        if (icon is null)
        {
            if (text is null) return;

            DrawCaption(g, content, this.HorizontalContentAlign, this.VerticalContentAlign);
            return;
        }

        Size textSize = text is null ? Size.Empty : TextMeasurer.Current.MeasureText(text, EffectiveFont);
        float gap = text is null ? 0f : IconGap;

        if (IconIsVertical)
        {
            // the icon and the text as one column, placed by the vertical alignment;
            // each of them centered across by the horizontal one
            float columnHeight = IconSize + gap + textSize.Height;
            float top = Align(content.Y, content.Height, columnHeight, this.VerticalContentAlign);

            float iconTop = IconPlacement == IconPlacement.Top ? top : top + textSize.Height + gap;
            float textTop = IconPlacement == IconPlacement.Top ? top + IconSize + gap : top;

            float iconLeft = Align(content.X, content.Width, IconSize, this.HorizontalContentAlign);

            icon.Draw(g, new Rectangle(new Point(iconLeft, iconTop), new Size(IconSize, IconSize)), CurrentTextColor);

            if (text is not null)
                DrawCaption(g,
                    new Rectangle(new Point(content.X, textTop), new Size(content.Width, textSize.Height)),
                    this.HorizontalContentAlign, VerticalContentAlignment.Center);

            return;
        }

        // the icon and the text as one row, placed by the horizontal alignment: a centered
        // button centers the pair, not the text in what the icon left over. A row wider
        // than the button starts at its edge, and the text gets what is left
        float rowWidth = IconSize + gap + textSize.Width;
        float left = rowWidth > content.Width
            ? content.X
            : Align(content.X, content.Width, rowWidth, this.HorizontalContentAlign);

        float iconY = Align(content.Y, content.Height, IconSize, this.VerticalContentAlign);
        float iconX = IconIsLeft ? left : Math.Min(left + textSize.Width + gap, content.X + content.Width - IconSize);

        icon.Draw(g, new Rectangle(new Point(iconX, iconY), new Size(IconSize, IconSize)), CurrentTextColor);

        if (text is null) return;

        float textX = IconIsLeft ? left + IconSize + gap : left;
        float textRight = IconIsLeft ? content.X + content.Width : iconX - gap;

        DrawCaption(g,
            new Rectangle(new Point(textX, content.Y), new Size(Math.Max(0, textRight - textX), content.Height)),
            HorizontalContentAlignment.Left, this.VerticalContentAlign);
    }

    private void DrawCaption(Graphics g, Rectangle area, HorizontalContentAlignment horizontal, VerticalContentAlignment vertical)
    {
        g.DrawText(ApplyTextTransform(Text!), area, CurrentTextColor, EffectiveFont, horizontal, vertical);
        DrawAccessKeyUnderline(g, Text!, area, CurrentTextColor, horizontal, vertical);
    }

    private static float Align(float start, float available, float size, HorizontalContentAlignment alignment) => alignment switch
    {
        HorizontalContentAlignment.Left => start,
        HorizontalContentAlignment.Right => start + available - size,
        _ => start + (available - size) / 2f,
    };

    private static float Align(float start, float available, float size, VerticalContentAlignment alignment) => alignment switch
    {
        VerticalContentAlignment.Top => start,
        VerticalContentAlignment.Bottom => start + available - size,
        _ => start + (available - size) / 2f,
    };

    protected override Size MeasureOverride(Size availableSize)
    {
        Size textSize = string.IsNullOrEmpty(Text)
            ? Size.Empty
            : TextMeasurer.Current.MeasureText(ApplyTextTransform(Text), EffectiveFont);

        float width = textSize.Width;
        float height = textSize.Height;

        if (Icon is not null)
        {
            float gap = textSize.Width > 0 ? IconGap : 0;

            if (IconIsVertical)
            {
                width = Math.Max(width, IconSize);
                height += IconSize + gap;
            }
            else
            {
                width += IconSize + gap;
                height = Math.Max(height, IconSize);
            }
        }

        return ResolveSize(new Size(width + Padding.Horizontal, height + Padding.Vertical), availableSize);
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

    // the caption as a key: the button follows the language
    public static PrimaryButton Primary(TextKey caption) => new PrimaryButton().Localize(caption);
    public static SecondaryButton Secondary(TextKey caption) => new SecondaryButton().Localize(caption);
    public static DangerButton Danger(TextKey caption) => new DangerButton().Localize(caption);
}