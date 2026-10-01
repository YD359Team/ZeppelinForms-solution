using ZeppelinForms.Drawing.Helpers;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Theming;

/// <summary>An accent color with the shades a theme draws with.</summary>
/// <remarks>
/// <para>
/// A light and a dark theme can't use one accent as it is: on a light page it must
/// be dark enough to carry white text, on a dark page light enough to carry black.
/// Windows keeps the shades in its accent palette, and WinUI takes
/// SystemAccentColorDark1 for its light theme and SystemAccentColorLight2 for the
/// dark one; <see cref="Dark1"/> and <see cref="Light2"/> are those. Android 12
/// gives the same from its tonal palette.
/// </para>
/// <para>
/// Where the system gives the accent alone — a browser, an older Android, a brand
/// color from the application — the shades are derived: the lightness is moved in
/// HSL, keeping the hue, by the amounts that turn the default Windows blue into its
/// own Dark1 and Light2, and then further, if needed, until the text on the shade
/// is readable. The hue is kept so that a green accent stays green rather than
/// sliding to grey on the way.
/// </para>
/// </remarks>
public sealed record AccentPalette(Color Accent)
{
    /// <summary>The shade for a light page — the accent of a light Fluent theme.
    /// Null — derive from <see cref="Accent"/>.</summary>
    public Color? Dark1 { get; init; }

    /// <summary>The shade for a dark page — the accent of a dark Fluent theme.
    /// Null — derive from <see cref="Accent"/>.</summary>
    public Color? Light2 { get; init; }

    /// <summary>The accent for a light page: <see cref="Dark1"/>, or derived.</summary>
    public Color ForLightPage() => Dark1 ?? Derive(Accent, -0.04f, Colors.White);

    /// <summary>The accent for a dark page: <see cref="Light2"/>, or derived.</summary>
    public Color ForDarkPage() => Light2 ?? Derive(Accent, +0.23f, Colors.Black);

    /// <summary>Move the lightness by <paramref name="step"/>, then on in the same
    /// direction until <paramref name="text"/> reads on the result at 4.5:1 —
    /// the WCAG threshold for normal text, which a button label is.</summary>
    private static Color Derive(Color accent, float step, Color text)
    {
        (float h, float s, float l) = ToHsl(accent);

        l = Math.Clamp(l + step, 0f, 1f);
        Color shade = FromHsl(h, s, l, accent.A);

        // the default blue needs none of this; a yellow or a pale accent does
        float direction = Math.Sign(step);

        for (int i = 0; i < 50 && shade.ContrastRatio(text) < 4.5f; i++)
        {
            l = Math.Clamp(l + direction * 0.02f, 0f, 1f);
            shade = FromHsl(h, s, l, accent.A);

            if (l is 0f or 1f) break;
        }

        return shade;
    }

    private static (float H, float S, float L) ToHsl(Color c)
    {
        float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f;
        float max = MathF.Max(r, MathF.Max(g, b));
        float min = MathF.Min(r, MathF.Min(g, b));
        float l = (max + min) / 2f;
        float d = max - min;

        if (d == 0f) return (0f, 0f, l);

        float s = d / (1f - MathF.Abs(2f * l - 1f));

        float h = max == r ? (g - b) / d % 6f
            : max == g ? (b - r) / d + 2f
            : (r - g) / d + 4f;

        h *= 60f;
        if (h < 0f) h += 360f;

        return (h, s, l);
    }

    private static Color FromHsl(float h, float s, float l, byte alpha)
    {
        float c = (1f - MathF.Abs(2f * l - 1f)) * s;
        float x = c * (1f - MathF.Abs(h / 60f % 2f - 1f));
        float m = l - c / 2f;

        (float r, float g, float b) = h switch
        {
            < 60f => (c, x, 0f),
            < 120f => (x, c, 0f),
            < 180f => (0f, c, x),
            < 240f => (0f, x, c),
            < 300f => (x, 0f, c),
            _ => (c, 0f, x),
        };

        static byte Channel(float v) => (byte)Math.Clamp(MathF.Round(v * 255f), 0f, 255f);

        return new Color(alpha, Channel(r + m), Channel(g + m), Channel(b + m));
    }
}