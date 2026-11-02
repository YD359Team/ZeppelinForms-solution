using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Theming;

/// <summary>
/// The colors of a Material 3 scheme, made from one seed color as Material You makes them:
/// tonal palettes of the seed's hue — the primary, a quieter secondary, the near-grey
/// neutrals — and each role a tone of one of them.
/// </summary>
/// <remarks>
/// <para>
/// Material measures tone as CIE L*, the lightness the eye sees, and hue and chroma in
/// CAM16. Here hue and chroma are CIE LCh's, the cylinder of L*a*b*: the tones are the
/// same, and the colors land within a few steps of Material's own — the baseline seed
/// #6750A4 gives its scheme to within a shade. A tone a hue can't reach at the chroma asked
/// for, the yellow ones high and the blue ones low, keeps its tone and gives up chroma
/// until it is a color of sRGB.
/// </para>
/// <para>
/// The roles and their tones are the scheme's of Material 3: primary 40 on light and 80 on
/// dark, surfaces from neutral 98 and 6, containers in between, outlines from the neutral
/// variant.
/// </para>
/// </remarks>
internal static class MaterialScheme
{
    /// <summary>The baseline seed of Material 3: the purple of its default scheme.</summary>
    public static readonly Color BaselineSeed = new(255, 0x67, 0x50, 0xA4);

    public static ThemeColors Create(Color seed, bool dark)
    {
        (double _, double chroma, double hue) = Lch.FromColor(seed);

        // a grey seed has no hue to speak of: the scheme keeps the baseline's
        if (chroma < 8) hue = Lch.FromColor(BaselineSeed).H;

        var primary = new TonalPalette(hue, Math.Max(chroma, 48));
        var secondary = new TonalPalette(hue, 16);
        var neutral = new TonalPalette(hue, 4);
        var neutralVariant = new TonalPalette(hue, 8);

        // what isn't the brand is the same whatever the seed: Material's error red, and
        // a green and an amber at the same tones. Given as colors rather than as palettes:
        // CAM16 chroma doesn't map on LCh's alike at every tone, and Material's own error
        // tones 40 and 80 are what is wanted
        var error = (Light: new Color(255, 0xB3, 0x26, 0x1E), Dark: new Color(255, 0xF2, 0xB8, 0xB5));
        var success = (Light: new Color(255, 0x38, 0x6A, 0x20), Dark: new Color(255, 0x9C, 0xD6, 0x7D));
        var warning = (Light: new Color(255, 0x7D, 0x57, 0x00), Dark: new Color(255, 0xF8, 0xBD, 0x48));

        return dark ? Dark() : Light();

        ThemeColors Light()
        {
            Color surface = neutral.Tone(98);
            Color container = neutral.Tone(94);
            Color onSurface = neutral.Tone(10);
            Color primaryColor = primary.Tone(40);
            Color onPrimary = primary.Tone(100);

            return new ThemeColors
            {
                Background = surface,
                Surface = container,
                SurfaceHover = StateLayer(container, onSurface, Hovered),
                SurfacePressed = StateLayer(container, onSurface, Pressed),

                Border = neutralVariant.Tone(80),                  // outlineVariant
                BorderFocused = primaryColor,

                Text = onSurface,
                TextSecondary = neutralVariant.Tone(30),           // onSurfaceVariant
                TextDisabled = StateLayer(surface, onSurface, Disabled),
                TextOnAccent = onPrimary,

                Accent = primaryColor,
                AccentHover = StateLayer(primaryColor, onPrimary, Hovered),
                AccentPressed = StateLayer(primaryColor, onPrimary, Pressed),

                Selection = secondary.Tone(90),                    // secondaryContainer
                TextSelection = secondary.Tone(10),                // onSecondaryContainer

                Success = success.Light,
                Warning = warning.Light,
                Error = error.Light,

                ScrollTrack = Colors.Transparent,
                ScrollThumb = neutralVariant.Tone(30).WithA(0x73),

                ControlFill = neutral.Tone(90),                    // surfaceContainerHighest
                ControlStroke = neutralVariant.Tone(50),           // outline
                ControlStrokeSecondary = neutralVariant.Tone(50),
                ControlStrongStroke = neutralVariant.Tone(30),     // onSurfaceVariant
                FocusStrokeOuter = secondary.Tone(40),             // secondary
                FocusStrokeInner = surface,
            };
        }

        ThemeColors Dark()
        {
            Color surface = neutral.Tone(6);
            Color container = neutral.Tone(12);
            Color onSurface = neutral.Tone(90);
            Color primaryColor = primary.Tone(80);
            Color onPrimary = primary.Tone(20);

            return new ThemeColors
            {
                Background = surface,
                Surface = container,
                SurfaceHover = StateLayer(container, onSurface, Hovered),
                SurfacePressed = StateLayer(container, onSurface, Pressed),

                Border = neutralVariant.Tone(30),
                BorderFocused = primaryColor,

                Text = onSurface,
                TextSecondary = neutralVariant.Tone(80),
                TextDisabled = StateLayer(surface, onSurface, Disabled),
                TextOnAccent = onPrimary,

                Accent = primaryColor,
                AccentHover = StateLayer(primaryColor, onPrimary, Hovered),
                AccentPressed = StateLayer(primaryColor, onPrimary, Pressed),

                Selection = secondary.Tone(30),
                TextSelection = secondary.Tone(90),

                Success = success.Dark,
                Warning = warning.Dark,
                Error = error.Dark,

                ScrollTrack = Colors.Transparent,
                ScrollThumb = neutralVariant.Tone(80).WithA(0x73),

                ControlFill = neutral.Tone(22),
                ControlStroke = neutralVariant.Tone(60),
                ControlStrokeSecondary = neutralVariant.Tone(60),
                ControlStrongStroke = neutralVariant.Tone(80),
                FocusStrokeOuter = secondary.Tone(80),
                FocusStrokeInner = surface,
            };
        }
    }

    // the opacities of Material's state layers: the content color laid over the container
    public const float Hovered = 0.08f;
    public const float Pressed = 0.10f;
    public const float Disabled = 0.38f;

    /// <summary>A state layer of <paramref name="content"/> over <paramref name="container"/>,
    /// worked out in advance as one opaque color.</summary>
    public static Color StateLayer(Color container, Color content, float opacity) =>
        Color.Lerp(container, content, opacity);

    /// <summary>The tones of one hue at one chroma.</summary>
    private readonly record struct TonalPalette(double Hue, double Chroma)
    {
        public Color Tone(double tone) => Lch.ToColor(tone, Chroma, Hue);
    }
}

/// <summary>CIE LCh: lightness, chroma and hue over L*a*b*, D65 white.</summary>
internal static class Lch
{
    private const double Xn = 0.95047, Yn = 1.0, Zn = 1.08883;

    public static (double L, double C, double H) FromColor(Color color)
    {
        double r = Linear(color.R / 255.0), g = Linear(color.G / 255.0), b = Linear(color.B / 255.0);

        double x = 0.4124564 * r + 0.3575761 * g + 0.1804375 * b;
        double y = 0.2126729 * r + 0.7151522 * g + 0.0721750 * b;
        double z = 0.0193339 * r + 0.1191920 * g + 0.9503041 * b;

        double fx = F(x / Xn), fy = F(y / Yn), fz = F(z / Zn);

        double l = 116 * fy - 16;
        double a = 500 * (fx - fy);
        double bb = 200 * (fy - fz);

        double hue = Math.Atan2(bb, a) * 180 / Math.PI;
        if (hue < 0) hue += 360;

        return (l, Math.Sqrt(a * a + bb * bb), hue);
    }

    /// <summary>The color of a tone, a chroma and a hue; past sRGB the chroma is lowered
    /// until it fits, the tone and the hue kept.</summary>
    public static Color ToColor(double tone, double chroma, double hue)
    {
        if (tone <= 0) return new Color(255, 0, 0, 0);
        if (tone >= 100) return new Color(255, 255, 255, 255);

        // the halving search: the largest chroma up to the one asked for that is in gamut
        if (TryRgb(tone, chroma, hue, out Color fits)) return fits;

        double low = 0, high = chroma;
        Color best = Grey(tone);

        for (int i = 0; i < 24; i++)
        {
            double mid = (low + high) / 2;

            if (TryRgb(tone, mid, hue, out Color color))
            {
                best = color;
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        return best;
    }

    private static Color Grey(double tone)
    {
        TryRgb(tone, 0, 0, out Color grey);
        return grey;
    }

    private static bool TryRgb(double l, double chroma, double hue, out Color color)
    {
        double radians = hue * Math.PI / 180;
        double a = chroma * Math.Cos(radians);
        double b = chroma * Math.Sin(radians);

        double fy = (l + 16) / 116;
        double fx = fy + a / 500;
        double fz = fy - b / 200;

        double x = Xn * InverseF(fx), y = Yn * InverseF(fy), z = Zn * InverseF(fz);

        double r = 3.2404542 * x - 1.5371385 * y - 0.4985314 * z;
        double g = -0.9692660 * x + 1.8760108 * y + 0.0415560 * z;
        double bl = 0.0556434 * x - 0.2040259 * y + 1.0572252 * z;

        const double slack = 0.0005;

        bool inGamut = r is >= -slack and <= 1 + slack && g is >= -slack and <= 1 + slack && bl is >= -slack and <= 1 + slack;

        color = new Color(255, Channel(r), Channel(g), Channel(bl));
        return inGamut;
    }

    private static byte Channel(double linear)
    {
        double v = Math.Clamp(linear, 0, 1);
        double srgb = v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;

        return (byte)Math.Clamp(Math.Round(srgb * 255), 0, 255);
    }

    private static double Linear(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    private static double F(double t) => t > 216.0 / 24389 ? Math.Cbrt(t) : (24389.0 / 27 * t + 16) / 116;

    private static double InverseF(double f)
    {
        double cube = f * f * f;
        return cube > 216.0 / 24389 ? cube : (116 * f - 16) / (24389.0 / 27);
    }
}
