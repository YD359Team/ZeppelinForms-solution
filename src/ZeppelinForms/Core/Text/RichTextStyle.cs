using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Core.Text;

/// <summary>Formatting applied to a run of characters in a rich document.</summary>
/// <remarks>
/// Unset members fall back to the control's defaults when the text is drawn.
/// Equality is by value so spans with the same look can be merged.
/// </remarks>
public readonly struct RichTextStyle : IEquatable<RichTextStyle>
{
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public bool Underline { get; init; }
    public bool Strikethrough { get; init; }

    /// <summary>Point size; 0 — use the control's font size.</summary>
    public float FontSize { get; init; }

    public Color? Foreground { get; init; }
    public Color? Background { get; init; }

    /// <summary>Family override; null — the control's font family.</summary>
    public string? FontFamily { get; init; }

    public static RichTextStyle Empty { get; }

    public bool IsEmpty =>
        !Bold && !Italic && !Underline && !Strikethrough &&
        FontSize <= 0 && Foreground is null && Background is null && FontFamily is null;

    public RichTextStyle WithBold(bool bold = true) => this with { Bold = bold };
    public RichTextStyle WithItalic(bool italic = true) => this with { Italic = italic };
    public RichTextStyle WithUnderline(bool underline = true) => this with { Underline = underline };
    public RichTextStyle WithForeground(Color color) => this with { Foreground = color };
    public RichTextStyle WithBackground(Color color) => this with { Background = color };

    /// <summary>Merge: the other style wins where it sets something explicit.</summary>
    public RichTextStyle Overlay(RichTextStyle other) => new()
    {
        Bold = other.Bold || Bold,
        Italic = other.Italic || Italic,
        Underline = other.Underline || Underline,
        Strikethrough = other.Strikethrough || Strikethrough,
        FontSize = other.FontSize > 0 ? other.FontSize : FontSize,
        Foreground = other.Foreground ?? Foreground,
        Background = other.Background ?? Background,
        FontFamily = other.FontFamily ?? FontFamily,
    };

    public Font ResolveFont(Font baseFont)
    {
        Font font = baseFont;

        if (FontFamily is { Length: > 0 })
            font = new Font(FontFamily, font.Size, font.Weight, font.Style);

        if (FontSize > 0)
            font = font.WithSize(FontSize);

        if (Bold)
            font = font.Bold();

        if (Italic)
            font = font.Italic();

        return font;
    }

    public TextRun ToRun(string text, Font baseFont, Color baseColor) =>
        new(text)
        {
            Font = ResolveFont(baseFont),
            Color = Foreground ?? baseColor,
            Background = Background,
            Underline = Underline,
            Strikethrough = Strikethrough,
        };

    public bool Equals(RichTextStyle other) =>
        Bold == other.Bold &&
        Italic == other.Italic &&
        Underline == other.Underline &&
        Strikethrough == other.Strikethrough &&
        FontSize.Equals(other.FontSize) &&
        Equals(Foreground, other.Foreground) &&
        Equals(Background, other.Background) &&
        FontFamily == other.FontFamily;

    public override bool Equals(object? obj) => obj is RichTextStyle other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Bold, Italic, Underline, Strikethrough, FontSize, Foreground, Background, FontFamily);

    public static bool operator ==(RichTextStyle left, RichTextStyle right) => left.Equals(right);
    public static bool operator !=(RichTextStyle left, RichTextStyle right) => !left.Equals(right);
}
