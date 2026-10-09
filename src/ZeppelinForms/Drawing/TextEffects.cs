using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Drawing;

/// <summary>Lines drawn with text, as CSS's <c>text-decoration-line</c>.
/// Combined: <c>Underline | Strikethrough</c>; in a style sheet —
/// <c>TextDecorations: underline strikethrough;</c>.</summary>
[Flags]
public enum TextDecorations : byte
{
    None = 0,

    /// <summary>Under the baseline, above the descenders.</summary>
    Underline = 1,

    /// <summary>Through the middle of the lowercase letters.</summary>
    Strikethrough = 2,

    /// <summary>Along the top of the capitals.</summary>
    Overline = 4,
}

/// <summary>What the text an element draws looks like beyond its font and color:
/// decoration lines and an outline. Set on <see cref="Graphics.TextEffects"/> by the
/// tree renderer for each element from its text properties, so every caption of a
/// control takes them without the control doing anything.</summary>
/// <param name="Decorations">The lines.</param>
/// <param name="DecorationColor">Their color; transparent — the text's own.</param>
/// <param name="OutlineColor">The outline around the glyphs; transparent — none.</param>
/// <param name="OutlineWidth">The outline's width outside the glyph, in pixels.</param>
public readonly record struct TextEffects(
    TextDecorations Decorations,
    Color DecorationColor,
    Color OutlineColor,
    float OutlineWidth)
{
    public static TextEffects None => default;

    public bool HasOutline => OutlineColor.A > 0 && OutlineWidth > 0f;

    public bool IsEmpty => Decorations == TextDecorations.None && !HasOutline;

    /// <summary>The decoration lines' color for text of <paramref name="textColor"/>.</summary>
    public Color LineColor(Color textColor) => DecorationColor.A > 0 ? DecorationColor : textColor;
}