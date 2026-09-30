using ZeppelinForms.Drawing;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Theming;

/// <summary>One step of a type ramp: the size and weight of a text style.
/// The family is not part of it — it comes from the element's font,
/// so a ramp fits any family the application chooses.</summary>
public readonly record struct TypeRampStep(float Size, FontWeight Weight);

/// <summary>A theme's type ramp: the sizes and weights of the text styles
/// an element picks by <see cref="TextStyle"/>.</summary>
/// <remarks>
/// <para>
/// Headings used to be made by hand at every place: a font of the right size,
/// bold, repeated on each label. A ramp names the step instead of restating it,
/// so the hierarchy of an interface is kept in one place and follows the theme.
/// </para>
/// <para>
/// The defaults are the type ramp of Fluent 2 (WinUI 3), in device-independent
/// pixels. The line heights of that ramp are left out: text controls compute the
/// line height from the font itself, and a ramp value nobody reads would only
/// pretend to work.
/// </para>
/// </remarks>
public sealed record TypeRamp
{
    /// <summary>The Fluent 2 ramp.</summary>
    public static TypeRamp Default { get; } = new();

    public TypeRampStep Caption { get; init; } = new(12f, FontWeight.Normal);
    public TypeRampStep Body { get; init; } = new(14f, FontWeight.Normal);
    public TypeRampStep BodyStrong { get; init; } = new(14f, FontWeight.SemiBold);
    public TypeRampStep BodyLarge { get; init; } = new(18f, FontWeight.Normal);
    public TypeRampStep Subtitle { get; init; } = new(20f, FontWeight.SemiBold);
    public TypeRampStep Title { get; init; } = new(28f, FontWeight.SemiBold);
    public TypeRampStep TitleLarge { get; init; } = new(40f, FontWeight.SemiBold);
    public TypeRampStep Display { get; init; } = new(68f, FontWeight.SemiBold);

    /// <summary>The step of a style. <see cref="TextStyle.None"/> has no step
    /// of its own — it means the element's font as it is — and asking for it
    /// is a mistake of the caller.</summary>
    public TypeRampStep this[TextStyle style] => style switch
    {
        TextStyle.Caption => Caption,
        TextStyle.Body => Body,
        TextStyle.BodyStrong => BodyStrong,
        TextStyle.BodyLarge => BodyLarge,
        TextStyle.Subtitle => Subtitle,
        TextStyle.Title => Title,
        TextStyle.TitleLarge => TitleLarge,
        TextStyle.Display => Display,
        _ => throw new ArgumentOutOfRangeException(nameof(style), style,
            "TextStyle.None has no step in the ramp: it stands for the element's own font."),
    };

    /// <summary>A font of the style over a base font: the family, slant
    /// and file are taken from the base, the size and weight — from the step.</summary>
    public Font Apply(Font baseFont, TextStyle style)
    {
        if (style == TextStyle.None) return baseFont;

        TypeRampStep step = this[style];

        return baseFont with { Size = step.Size, Weight = step.Weight };
    }
}