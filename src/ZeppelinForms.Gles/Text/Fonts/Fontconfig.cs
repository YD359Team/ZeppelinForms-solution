using System.Runtime.InteropServices;
using System.Text;

namespace ZeppelinForms.Gles.Text.Fonts;

/// <summary>
/// The desktop's own choice of fonts on Linux, through fontconfig — the library every
/// Linux desktop configures its fonts with, and Skia's font manager there: what
/// "sans-serif" is, which face of a family is bold, which font has a character.
/// </summary>
/// <remarks>
/// <para>
/// Loaded at run time and optional: where the library is missing — a container, another
/// system — the directory catalog decides instead. Only matching is asked of it; the files
/// it names are read by the managed reader.
/// </para>
/// <para>
/// Calls are serialized: fontconfig's configuration is shared, and its matching was not
/// safe from several threads in every version still shipped.
/// </para>
/// </remarks>
internal static unsafe class Fontconfig
{
    private const int FcResultMatch = 0;
    private const int FcMatchPattern = 0;

    private static readonly Lock Sync = new();

    private static readonly nint Library = Load();
    private static nint s_config;

    private static readonly delegate* unmanaged<nint> FcInitLoadConfigAndFonts;
    private static readonly delegate* unmanaged<nint> FcPatternCreate;
    private static readonly delegate* unmanaged<nint, void> FcPatternDestroy;
    private static readonly delegate* unmanaged<nint, byte*, byte*, int> FcPatternAddString;
    private static readonly delegate* unmanaged<nint, byte*, int, int> FcPatternAddInteger;
    private static readonly delegate* unmanaged<nint, byte*, nint, int> FcPatternAddCharSet;
    private static readonly delegate* unmanaged<nint, byte*, int, byte**, int> FcPatternGetString;
    private static readonly delegate* unmanaged<nint, byte*, int, int*, int> FcPatternGetInteger;
    private static readonly delegate* unmanaged<nint, nint, int, int> FcConfigSubstitute;
    private static readonly delegate* unmanaged<nint, void> FcDefaultSubstitute;
    private static readonly delegate* unmanaged<nint, nint, int*, nint> FcFontMatch;
    private static readonly delegate* unmanaged<nint> FcCharSetCreate;
    private static readonly delegate* unmanaged<nint, uint, int> FcCharSetAddChar;
    private static readonly delegate* unmanaged<nint, void> FcCharSetDestroy;

    static Fontconfig()
    {
        if (Library == 0) return;

        try
        {
            FcInitLoadConfigAndFonts = (delegate* unmanaged<nint>)NativeLibrary.GetExport(Library, "FcInitLoadConfigAndFonts");
            FcPatternCreate = (delegate* unmanaged<nint>)NativeLibrary.GetExport(Library, "FcPatternCreate");
            FcPatternDestroy = (delegate* unmanaged<nint, void>)NativeLibrary.GetExport(Library, "FcPatternDestroy");
            FcPatternAddString = (delegate* unmanaged<nint, byte*, byte*, int>)NativeLibrary.GetExport(Library, "FcPatternAddString");
            FcPatternAddInteger = (delegate* unmanaged<nint, byte*, int, int>)NativeLibrary.GetExport(Library, "FcPatternAddInteger");
            FcPatternAddCharSet = (delegate* unmanaged<nint, byte*, nint, int>)NativeLibrary.GetExport(Library, "FcPatternAddCharSet");
            FcPatternGetString = (delegate* unmanaged<nint, byte*, int, byte**, int>)NativeLibrary.GetExport(Library, "FcPatternGetString");
            FcPatternGetInteger = (delegate* unmanaged<nint, byte*, int, int*, int>)NativeLibrary.GetExport(Library, "FcPatternGetInteger");
            FcConfigSubstitute = (delegate* unmanaged<nint, nint, int, int>)NativeLibrary.GetExport(Library, "FcConfigSubstitute");
            FcDefaultSubstitute = (delegate* unmanaged<nint, void>)NativeLibrary.GetExport(Library, "FcDefaultSubstitute");
            FcFontMatch = (delegate* unmanaged<nint, nint, int*, nint>)NativeLibrary.GetExport(Library, "FcFontMatch");
            FcCharSetCreate = (delegate* unmanaged<nint>)NativeLibrary.GetExport(Library, "FcCharSetCreate");
            FcCharSetAddChar = (delegate* unmanaged<nint, uint, int>)NativeLibrary.GetExport(Library, "FcCharSetAddChar");
            FcCharSetDestroy = (delegate* unmanaged<nint, void>)NativeLibrary.GetExport(Library, "FcCharSetDestroy");

            IsAvailable = true;
        }
        catch (EntryPointNotFoundException)
        {
            IsAvailable = false;
        }
    }

    /// <summary>The library loaded and has what is asked of it. ZF_FONTCONFIG=0 turns it off,
    /// to try the directory catalog on a desktop that has it.</summary>
    public static bool IsAvailable { get; }

    private static nint Load()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsFreeBSD()) return 0;

        if (Environment.GetEnvironmentVariable("ZF_FONTCONFIG") == "0") return 0;

        foreach (string name in (ReadOnlySpan<string>)["libfontconfig.so.1", "libfontconfig.so"])
            if (NativeLibrary.TryLoad(name, out nint handle))
                return handle;

        return 0;
    }

    /// <summary>The face fontconfig picks for a family and a style, and optionally a
    /// character it must have; null where it has none at all.</summary>
    /// <param name="family">A family, or a generic one: sans-serif, serif, monospace, emoji.</param>
    /// <param name="weight">The OpenType weight, 100 to 900.</param>
    public static FontMatch? Match(string family, int weight, bool italic, int? codepoint = null)
    {
        if (!IsAvailable) return null;

        lock (Sync)
        {
            if (s_config == 0)
            {
                s_config = FcInitLoadConfigAndFonts();
                if (s_config == 0) return null;
            }

            nint pattern = FcPatternCreate();
            nint charset = 0;

            try
            {
                fixed (byte* familyKey = "family\0"u8, weightKey = "weight\0"u8, slantKey = "slant\0"u8, charsetKey = "charset\0"u8)
                {
                    byte[] name = Encoding.UTF8.GetBytes(family + "\0");

                    fixed (byte* value = name)
                        FcPatternAddString(pattern, familyKey, value);

                    FcPatternAddInteger(pattern, weightKey, ToFontconfigWeight(weight));
                    FcPatternAddInteger(pattern, slantKey, italic ? 100 : 0);

                    if (codepoint is { } c)
                    {
                        charset = FcCharSetCreate();
                        FcCharSetAddChar(charset, (uint)c);
                        FcPatternAddCharSet(pattern, charsetKey, charset);
                    }
                }

                FcConfigSubstitute(s_config, pattern, FcMatchPattern);
                FcDefaultSubstitute(pattern);

                int result;
                nint match = FcFontMatch(s_config, pattern, &result);

                if (match == 0) return null;

                try
                {
                    return Read(match);
                }
                finally
                {
                    FcPatternDestroy(match);
                }
            }
            finally
            {
                FcPatternDestroy(pattern);
                if (charset != 0) FcCharSetDestroy(charset);
            }
        }
    }

    private static FontMatch? Read(nint match)
    {
        string? file = GetString(match, "file\0"u8);
        if (file is null) return null;

        // a named instance of a variable font is the face's index with the instance above
        // it: the default instance is read, the rest synthesized
        int index = (GetInteger(match, "index\0"u8) ?? 0) & 0xFFFF;
        string family = GetString(match, "family\0"u8) ?? string.Empty;
        int weight = ToOpenTypeWeight(GetInteger(match, "weight\0"u8) ?? 80);
        bool italic = (GetInteger(match, "slant\0"u8) ?? 0) != 0;

        return new FontMatch(file, index, family, weight, italic);
    }

    private static string? GetString(nint pattern, ReadOnlySpan<byte> key)
    {
        byte* value;

        fixed (byte* k = key)
            if (FcPatternGetString(pattern, k, 0, &value) != FcResultMatch) return null;

        return Marshal.PtrToStringUTF8((nint)value);
    }

    private static int? GetInteger(nint pattern, ReadOnlySpan<byte> key)
    {
        int value;

        fixed (byte* k = key)
            return FcPatternGetInteger(pattern, k, 0, &value) == FcResultMatch ? value : null;
    }

    // fontconfig's weights against OpenType's: thin 0, light 50, regular 80, medium 100,
    // semibold 180, bold 200, extrabold 205, black 210
    private static readonly (int OpenType, int Fontconfig)[] Weights =
    [
        (100, 0), (200, 40), (300, 50), (350, 55), (380, 75), (400, 80),
        (500, 100), (600, 180), (700, 200), (800, 205), (900, 210), (1000, 215),
    ];

    internal static int ToFontconfigWeight(int weight) => Interpolate(weight, fromOpenType: true);

    internal static int ToOpenTypeWeight(int weight) => Interpolate(weight, fromOpenType: false);

    private static int Interpolate(int value, bool fromOpenType)
    {
        for (int i = 0; i < Weights.Length - 1; i++)
        {
            (int a0, int b0) = Weights[i];
            (int a1, int b1) = Weights[i + 1];

            if (!fromOpenType)
            {
                (a0, b0) = (b0, a0);
                (a1, b1) = (b1, a1);
            }

            if (value <= a1)
            {
                if (value <= a0) return b0;
                return b0 + (value - a0) * (b1 - b0) / (a1 - a0);
            }
        }

        return fromOpenType ? Weights[^1].Fontconfig : Weights[^1].OpenType;
    }
}

/// <summary>A face a font source picked: its file and index, and its family and style as
/// the source knows them.</summary>
internal sealed record FontMatch(string Path, int Index, string Family, int Weight, bool Italic);