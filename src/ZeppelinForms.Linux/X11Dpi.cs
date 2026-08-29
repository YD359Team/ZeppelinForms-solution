using System.Globalization;
using System.Runtime.InteropServices;

namespace ZeppelinForms.Linux;

internal static class X11Dpi
{
    private const float BaseDpi = 96f;

    /// <summary>
    /// Масштаб интерфейса. Источники по убыванию надёжности:
    /// Xft.dpi из ресурсов X, переменные окружения, физический размер экрана.
    /// </summary>
    public static float GetScale(nint display, int screen)
    {
        if (TryFromResources(display, out float fromResources))
            return Normalize(fromResources / BaseDpi);

        if (TryFromEnvironment(out float fromEnv))
            return Normalize(fromEnv);

        if (TryFromPhysicalSize(display, screen, out float fromPhysical))
            return Normalize(fromPhysical / BaseDpi);

        return 1f;
    }

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

            // значение может быть дробным; культура тут всегда инвариантная
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

        // GDK_SCALE целочисленный, QT_SCALE_FACTOR может быть дробным
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

        // многие драйверы врут о физическом размере, выдавая абсурдные значения
        return dpi is > 50f and < 400f;
    }

    /// <summary>
    /// Округляем до четверти: интерфейс на 1.25 или 1.5 выглядит нормально,
    /// а на 1.0417 из кривого EDID — размыто и вкривь.
    /// </summary>
    private static float Normalize(float scale)
    {
        if (scale <= 0) return 1f;

        float rounded = MathF.Round(scale * 4f) / 4f;
        return Math.Clamp(rounded, 1f, 4f);
    }
}
