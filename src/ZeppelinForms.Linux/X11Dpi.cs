using System.Globalization;
using System.Runtime.InteropServices;

namespace ZeppelinForms.Linux;

internal static class X11Dpi
{
    private const float BaseDpi = 96f;

    /// <summary>
    /// The interface scale. Sources in decreasing order of reliability:
    /// Xft.dpi from the X resources, environment variables, the screen's physical size.
    /// </summary>
    /// <param name="textScaleInXftDpi">The text size the desktop folded into Xft.dpi:
    /// GNOME's settings daemon writes 96 × text scaling × window scale there. It is
    /// taken out — a text size is App.TextScale's, applied to text alone, while this
    /// scale enlarges everything. Until 0.21 "Large Text" on GNOME made every icon,
    /// padding and stroke a quarter larger as well. 1 — nothing folded in.</param>
    public static float GetScale(nint display, int screen, float textScaleInXftDpi)
    {
        if (TryFromResources(display, out float fromResources))
            return Normalize(fromResources / TextScaleOut(textScaleInXftDpi) / BaseDpi);

        if (TryFromEnvironment(out float fromEnv))
            return Normalize(fromEnv);

        if (TryFromPhysicalSize(display, screen, out float fromPhysical))
            return Normalize(fromPhysical / BaseDpi);

        return 1f;
    }

    /// <summary>The pixel density without rounding to a quarter and without clamping
    /// to [1, 4] — unlike GetScale, which coarsens the result on purpose so that the
    /// interface isn't drawn crooked. Gesture thresholds need the raw density.</summary>
    public static float GetDpi(nint display, int screen, float textScaleInXftDpi)
    {
        if (TryFromResources(display, out float fromResources))
            return fromResources / TextScaleOut(textScaleInXftDpi);

        if (TryFromPhysicalSize(display, screen, out float fromPhysical))
            return fromPhysical;

        // environment variables set a scale, not a density: we restore it by the
        // inverse operation — if we lie, we lie consistently with what GetScale returns
        if (TryFromEnvironment(out float fromEnv))
            return fromEnv * BaseDpi;

        return BaseDpi;
    }

    /// <summary>The text size to divide Xft.dpi by: a broken one counts as none.</summary>
    private static float TextScaleOut(float textScaleInXftDpi) =>
        float.IsFinite(textScaleInXftDpi) && textScaleInXftDpi > 0f ? textScaleInXftDpi : 1f;

    private static bool TryFromResources(nint display, out float dpi)
    {
        dpi = 0;

        nint resourceString = X11.XResourceManagerString(display);
        if (resourceString == 0) return false;

        string? resources = Marshal.PtrToStringAnsi(resourceString);
        if (string.IsNullOrEmpty(resources)) return false;

        X11.XrmInitialize();
        nint database = X11.XrmGetStringDatabase(resources);
        if (database == 0) return false;

        try
        {
            if (!X11.XrmGetResource(database, "Xft.dpi", "Xft.Dpi", out _, out X11.XrmValue value))
                return false;

            if (value.Address == 0) return false;

            string? text = Marshal.PtrToStringAnsi(value.Address);

            // the value may be fractional; the culture here is always invariant
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out dpi)
                && dpi > 0;
        }
        finally
        {
            X11.XrmDestroyDatabase(database);
        }
    }

    private static bool TryFromEnvironment(out float scale)
    {
        scale = 0;

        // GDK_SCALE is an integer, QT_SCALE_FACTOR may be fractional
        string?[] candidates =
        [
            Environment.GetEnvironmentVariable("QT_SCALE_FACTOR"),
            Environment.GetEnvironmentVariable("GDK_SCALE"),
        ];

        foreach (string? candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) &&
                float.TryParse(candidate, NumberStyles.Float, CultureInfo.InvariantCulture, out scale) &&
                scale > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryFromPhysicalSize(nint display, int screen, out float dpi)
    {
        dpi = 0;

        int pixels = X11.XDisplayWidth(display, screen);
        int millimeters = X11.XDisplayWidthMM(display, screen);

        if (pixels <= 0 || millimeters <= 0) return false;

        dpi = pixels * 25.4f / millimeters;

        // many drivers lie about the physical size, giving absurd values
        return dpi is > 50f and < 400f;
    }

    /// <summary>
    /// Rounded to a quarter: an interface at 1.25 or 1.5 looks fine,
    /// while at 1.0417 from a crooked EDID it is blurry and askew.
    /// </summary>
    private static float Normalize(float scale)
    {
        if (scale <= 0) return 1f;

        float rounded = MathF.Round(scale * 4f) / 4f;
        return Math.Clamp(rounded, 1f, 4f);
    }
}