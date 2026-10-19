namespace ZeppelinForms.Drawing;

/// <summary>How an element, or a picture, is put over what is already drawn under it.</summary>
/// <remarks>
/// <para>
/// The modes of Avalonia's <c>BitmapBlendingMode</c> and of CSS <c>mix-blend-mode</c>:
/// the separable ones — Multiply darkens, Screen lightens, Overlay adds contrast —
/// the non-separable ones that take hue, saturation or luminosity from one side, and
/// the Porter–Duff compositing operators, which decide by coverage what of either
/// side stays: DestinationOut cuts the element's shape out of what is under it.
/// </para>
/// <para>
/// An element with a mode other than <see cref="Normal"/> is drawn, with its
/// descendants, into a layer of its own, and the layer is put over what is under it
/// in that mode. "What is under it" ends at the nearest ancestor with a layer of its
/// own — an opacity below 1, a mode, an effect: the blend stays inside it, as CSS
/// isolates a stacking context.
/// </para>
/// </remarks>
public enum BlendMode
{
    /// <summary>Drawn over what is under it, as everything is by default.</summary>
    Normal,

    // ===== separable: channel by channel =====

    /// <summary>The channels multiplied: never lighter, white changes nothing.</summary>
    Multiply,

    /// <summary>The inverse of multiplying the inverses: never darker, black changes nothing.</summary>
    Screen,

    /// <summary>Multiply in the dark parts of what is under it, Screen in the light ones.</summary>
    Overlay,

    /// <summary>The darker of the two, channel by channel.</summary>
    Darken,

    /// <summary>The lighter of the two, channel by channel.</summary>
    Lighten,

    /// <summary>What is under it brightened to reflect the element.</summary>
    ColorDodge,

    /// <summary>What is under it darkened to reflect the element.</summary>
    ColorBurn,

    /// <summary>Overlay with the sides swapped: the element decides between Multiply and Screen.</summary>
    HardLight,

    /// <summary>A softer HardLight: darkens or lightens, as a diffused spotlight would.</summary>
    SoftLight,

    /// <summary>The darker subtracted from the lighter: black changes nothing, white inverts.</summary>
    Difference,

    /// <summary>Like Difference, with less contrast.</summary>
    Exclusion,

    /// <summary>The channels added, clamped: light upon light.</summary>
    Plus,

    // ===== non-separable: hue, saturation, luminosity =====

    /// <summary>The element's hue with the saturation and luminosity of what is under it.</summary>
    Hue,

    /// <summary>The element's saturation with the hue and luminosity of what is under it.</summary>
    Saturation,

    /// <summary>The element's hue and saturation with the luminosity of what is under it:
    /// a tint that keeps the shading.</summary>
    Color,

    /// <summary>The element's luminosity with the hue and saturation of what is under it.</summary>
    Luminosity,

    // ===== Porter–Duff compositing =====

    /// <summary>The element only; what is under it is gone where the element is.</summary>
    Source,

    /// <summary>What is under it only: the element isn't seen.</summary>
    Destination,

    /// <summary>What is under it over the element: the element shows only where nothing is.</summary>
    DestinationOver,

    /// <summary>The element where what is under it is, nothing elsewhere.</summary>
    SourceIn,

    /// <summary>What is under it where the element is: the element is a mask.</summary>
    DestinationIn,

    /// <summary>The element where nothing is under it.</summary>
    SourceOut,

    /// <summary>What is under it where the element isn't: the element's shape is cut out.</summary>
    DestinationOut,

    /// <summary>The element over what is under it, only where that is.</summary>
    SourceAtop,

    /// <summary>What is under it over the element, only where the element is.</summary>
    DestinationAtop,

    /// <summary>Either side where the other isn't.</summary>
    Xor,
}
