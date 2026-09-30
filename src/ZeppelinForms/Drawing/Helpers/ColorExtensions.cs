using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Drawing.Helpers;

public static class ColorExtensions
{
    public static Color Darken(this Color c, float amount = 0.15f)
    {
        byte Adjust(byte v) => (byte)Math.Clamp(v * (1 - amount), 0, 255);
        return new Color(c.A, Adjust(c.R), Adjust(c.G), Adjust(c.B));
    }

    public static Color Lighten(this Color c, float amount = 0.15f)
    {
        byte Adjust(byte v) => (byte)Math.Clamp(v + (255 - v) * amount, 0, 255);
        return new Color(c.A, Adjust(c.R), Adjust(c.G), Adjust(c.B));
    }

    /// <summary>Relative luminance by WCAG 2: 0 for black, 1 for white.
    /// Alpha is ignored — the color is taken as opaque.</summary>
    /// <remarks>
    /// Not the average of the channels: the eye is most sensitive to green and
    /// least to blue, and the channels are gamma-encoded, so they are linearized
    /// first. By the plain average, pure blue and pure yellow would be equally
    /// bright, while one takes white text and the other black.
    /// </remarks>
    public static float RelativeLuminance(this Color c)
    {
        static float Linear(byte channel)
        {
            float v = channel / 255f;
            return v <= 0.04045f ? v / 12.92f : MathF.Pow((v + 0.055f) / 1.055f, 2.4f);
        }

        return 0.2126f * Linear(c.R) + 0.7152f * Linear(c.G) + 0.0722f * Linear(c.B);
    }

    /// <summary>Contrast ratio by WCAG 2: from 1 (no contrast) to 21 (black on white).
    /// Symmetric — the order of the colors doesn't matter.</summary>
    public static float ContrastRatio(this Color a, Color b)
    {
        float la = a.RelativeLuminance();
        float lb = b.RelativeLuminance();

        return (MathF.Max(la, lb) + 0.05f) / (MathF.Min(la, lb) + 0.05f);
    }
}