using System.Runtime.InteropServices;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Theming;

namespace ZeppelinForms.Windows;

/// <summary>The appearance settings of Windows: the app mode, the accent palette,
/// and the dark title bar.</summary>
/// <remarks>
/// Read from the registry, not through WinRT's UISettings: the same values, without
/// activating WinRT from a plain Win32 process for three numbers. Both keys are
/// written by the Settings app and announced with WM_SETTINGCHANGE, which the
/// platform already listens to.
/// </remarks>
internal static class Win32Appearance
{
    private static readonly nint HKEY_CURRENT_USER = unchecked((nint)0x80000001);

    private const uint RRF_RT_REG_DWORD = 0x00000010;
    private const uint RRF_RT_REG_BINARY = 0x00000008;

    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AccentKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
    private const string DwmKey = @"Software\Microsoft\Windows\DWM";

    /// <summary>Windows 10 20H1 and later.</summary>
    private const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    /// <summary>The same attribute under its number before 20H1 (1809–1909).</summary>
    private const uint DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegGetValueW")]
    private static extern int RegGetDword(
        nint hkey, string subKey, string value, uint flags, out uint type, out uint data, ref uint size);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegGetValueW")]
    private static extern int RegGetBinary(
        nint hkey, string subKey, string value, uint flags, out uint type, [Out] byte[] data, ref uint size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, uint attribute, ref int value, uint size);

    // ===== high contrast =====

    private const uint SPI_GETHIGHCONTRAST = 0x0042;
    private const uint HCF_HIGHCONTRASTON = 0x00000001;

    private const int COLOR_WINDOW = 5;
    private const int COLOR_WINDOWTEXT = 8;
    private const int COLOR_HIGHLIGHT = 13;
    private const int COLOR_HIGHLIGHTTEXT = 14;
    private const int COLOR_BTNFACE = 15;
    private const int COLOR_GRAYTEXT = 17;
    private const int COLOR_BTNTEXT = 18;
    private const int COLOR_HOTLIGHT = 26;

    [StructLayout(LayoutKind.Sequential)]
    private struct HIGHCONTRAST
    {
        public uint cbSize;
        public uint dwFlags;
        public nint lpszDefaultScheme;
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    private static extern bool SystemParametersInfo(uint action, uint param, ref HIGHCONTRAST value, uint winIni);

    [DllImport("user32.dll")]
    private static extern uint GetSysColor(int index);

    /// <summary>A contrast theme is on: "Contrast themes" in Settings, or the
    /// Left Alt+Left Shift+Print Screen shortcut.</summary>
    internal static bool IsHighContrast()
    {
        var info = new HIGHCONTRAST { cbSize = (uint)Marshal.SizeOf<HIGHCONTRAST>() };

        return SystemParametersInfo(SPI_GETHIGHCONTRAST, info.cbSize, ref info, 0) &&
            (info.dwFlags & HCF_HIGHCONTRASTON) != 0;
    }

    /// <summary>The system colors of the contrast theme in effect — the user's
    /// own choice of them, which the theme must use as they are.</summary>
    internal static HighContrastPalette ReadHighContrastPalette() => new(
        Window: SysColor(COLOR_WINDOW),
        WindowText: SysColor(COLOR_WINDOWTEXT),
        ButtonFace: SysColor(COLOR_BTNFACE),
        ButtonText: SysColor(COLOR_BTNTEXT),
        Highlight: SysColor(COLOR_HIGHLIGHT),
        HighlightText: SysColor(COLOR_HIGHLIGHTTEXT),
        GrayText: SysColor(COLOR_GRAYTEXT),
        Hotlight: SysColor(COLOR_HOTLIGHT));

    /// <summary>A COLORREF is 0x00BBGGRR.</summary>
    private static Color SysColor(int index)
    {
        uint value = GetSysColor(index);

        return new Color((byte)value, (byte)(value >> 8), (byte)(value >> 16));
    }

    /// <summary>"Choose your default app mode" is Dark. Missing value — light:
    /// Windows before 1809 has no dark mode for applications.</summary>
    internal static bool AppsUseDarkTheme() =>
        ReadDword(PersonalizeKey, "AppsUseLightTheme") is 0u;

    /// <summary>The user's accent with its shades.</summary>
    /// <remarks>
    /// AccentPalette holds eight colors of four bytes, R, G, B and an unused byte:
    /// Light3, Light2, Light1, the accent, Dark1, Dark2, Dark3, and one more. They are
    /// the very values UISettings returns as AccentLight2, Accent and AccentDark1.
    /// Without the palette — the DWM accent, as ABGR, and the shades are derived.
    /// </remarks>
    internal static AccentPalette? ReadAccentPalette()
    {
        byte[] data = new byte[32];
        uint size = (uint)data.Length;

        if (RegGetBinary(HKEY_CURRENT_USER, AccentKey, "AccentPalette",
                RRF_RT_REG_BINARY, out _, data, ref size) == 0 && size >= 20)
        {
            return new AccentPalette(Entry(data, 3))
            {
                Light2 = Entry(data, 1),
                Dark1 = Entry(data, 4),
            };
        }

        if (ReadDword(DwmKey, "AccentColor") is uint abgr)
        {
            return new AccentPalette(new Color(
                (byte)abgr,
                (byte)(abgr >> 8),
                (byte)(abgr >> 16)));
        }

        return null;
    }

    /// <summary>Paint the window's title bar dark or light. The system draws the
    /// caption itself, and without this a dark theme sat under a white caption.</summary>
    internal static void SetDarkCaption(nint hwnd, bool dark)
    {
        int value = dark ? 1 : 0;

        // a failure is not an error: before 1809 there is no dark caption at all
        if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref value, sizeof(int));
    }

    private static Color Entry(byte[] data, int index) =>
        new(data[index * 4], data[index * 4 + 1], data[index * 4 + 2]);

    private static uint? ReadDword(string key, string value)
    {
        uint size = sizeof(uint);

        return RegGetDword(HKEY_CURRENT_USER, key, value, RRF_RT_REG_DWORD, out _, out uint data, ref size) == 0
            ? data
            : null;
    }
}