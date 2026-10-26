using System.Runtime.InteropServices;
using ZeppelinForms.Gles.Text.OpenType;

namespace ZeppelinForms.Gles.Text.Shaping;

/// <summary>
/// Text shaping by the system's HarfBuzz, loaded at run time: the glyphs of joined Arabic
/// letters, Indic vowel signs in their place, marks on their letters, emoji sequences
/// composed into one picture.
/// </summary>
/// <remarks>
/// <para>
/// HarfBuzz is a library of every Linux desktop — GTK, Qt and the browsers shape with it —
/// and of HarfBuzzSharp's native assets, whose library exports the same functions; an
/// application that ships those is shaped on Windows too. Where neither is found, text is
/// drawn a glyph per character: Latin, Cyrillic, Greek and CJK lose nothing, complex
/// scripts are drawn unjoined, as Skia's backend draws them without HarfBuzz.
/// </para>
/// <para>
/// A HarfBuzz font is made once per face and kept, over the face's pinned bytes: its
/// tables are read in place. Shaping with one font is safe from several threads; each
/// call has a buffer of its own.
/// </para>
/// </remarks>
internal static unsafe class HarfBuzz
{
    private const int MemoryModeReadOnly = 1;
    private const int DirectionLtr = 4;
    private const int DirectionRtl = 5;

    private static readonly nint Library = Load();

    private static readonly delegate* unmanaged<byte*, uint, int, nint, nint, nint> hb_blob_create;
    private static readonly delegate* unmanaged<nint, void> hb_blob_destroy;
    private static readonly delegate* unmanaged<nint, uint, nint> hb_face_create;
    private static readonly delegate* unmanaged<nint, void> hb_face_destroy;
    private static readonly delegate* unmanaged<nint, nint> hb_font_create;
    private static readonly delegate* unmanaged<nint, int, int, void> hb_font_set_scale;
    private static readonly delegate* unmanaged<nint> hb_buffer_create;
    private static readonly delegate* unmanaged<nint, void> hb_buffer_destroy;
    private static readonly delegate* unmanaged<nint, char*, int, uint, int, void> hb_buffer_add_utf16;
    private static readonly delegate* unmanaged<nint, int, void> hb_buffer_set_direction;
    private static readonly delegate* unmanaged<nint, void> hb_buffer_guess_segment_properties;
    private static readonly delegate* unmanaged<nint, nint, nint, uint, void> hb_shape;
    private static readonly delegate* unmanaged<nint, uint*, GlyphInfo*> hb_buffer_get_glyph_infos;
    private static readonly delegate* unmanaged<nint, uint*, GlyphPosition*> hb_buffer_get_glyph_positions;

    private static readonly Dictionary<int, nint> Fonts = [];
    private static readonly Lock FontsLock = new();

    static HarfBuzz()
    {
        if (Library == 0) return;

        try
        {
            hb_blob_create = (delegate* unmanaged<byte*, uint, int, nint, nint, nint>)NativeLibrary.GetExport(Library, "hb_blob_create");
            hb_blob_destroy = (delegate* unmanaged<nint, void>)NativeLibrary.GetExport(Library, "hb_blob_destroy");
            hb_face_create = (delegate* unmanaged<nint, uint, nint>)NativeLibrary.GetExport(Library, "hb_face_create");
            hb_face_destroy = (delegate* unmanaged<nint, void>)NativeLibrary.GetExport(Library, "hb_face_destroy");
            hb_font_create = (delegate* unmanaged<nint, nint>)NativeLibrary.GetExport(Library, "hb_font_create");
            hb_font_set_scale = (delegate* unmanaged<nint, int, int, void>)NativeLibrary.GetExport(Library, "hb_font_set_scale");
            hb_buffer_create = (delegate* unmanaged<nint>)NativeLibrary.GetExport(Library, "hb_buffer_create");
            hb_buffer_destroy = (delegate* unmanaged<nint, void>)NativeLibrary.GetExport(Library, "hb_buffer_destroy");
            hb_buffer_add_utf16 = (delegate* unmanaged<nint, char*, int, uint, int, void>)NativeLibrary.GetExport(Library, "hb_buffer_add_utf16");
            hb_buffer_set_direction = (delegate* unmanaged<nint, int, void>)NativeLibrary.GetExport(Library, "hb_buffer_set_direction");
            hb_buffer_guess_segment_properties = (delegate* unmanaged<nint, void>)NativeLibrary.GetExport(Library, "hb_buffer_guess_segment_properties");
            hb_shape = (delegate* unmanaged<nint, nint, nint, uint, void>)NativeLibrary.GetExport(Library, "hb_shape");
            hb_buffer_get_glyph_infos = (delegate* unmanaged<nint, uint*, GlyphInfo*>)NativeLibrary.GetExport(Library, "hb_buffer_get_glyph_infos");
            hb_buffer_get_glyph_positions = (delegate* unmanaged<nint, uint*, GlyphPosition*>)NativeLibrary.GetExport(Library, "hb_buffer_get_glyph_positions");

            IsAvailable = true;
        }
        catch (EntryPointNotFoundException)
        {
            IsAvailable = false;
        }
    }

    /// <summary>HarfBuzz was found. ZF_HARFBUZZ=0 turns it off, to see text drawn unshaped.</summary>
    public static bool IsAvailable { get; }

    private static nint Load()
    {
        if (Environment.GetEnvironmentVariable("ZF_HARFBUZZ") == "0") return 0;

        ReadOnlySpan<string> names = OperatingSystem.IsWindows()
            ? ["harfbuzz", "libHarfBuzzSharp"]
            : OperatingSystem.IsMacOS()
                ? ["libharfbuzz.0.dylib", "libharfbuzz.dylib", "libHarfBuzzSharp"]
                : ["libharfbuzz.so.0", "libharfbuzz.so", "libHarfBuzzSharp"];

        foreach (string name in names)
        {
            if (NativeLibrary.TryLoad(name, out nint handle)) return handle;

            // the application's own folder, where a NuGet native asset lands
            if (NativeLibrary.TryLoad(name, typeof(HarfBuzz).Assembly, null, out handle)) return handle;
        }

        return 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GlyphInfo
    {
        public uint Codepoint;
        public uint Mask;
        public uint Cluster;
        private uint _var1;
        private uint _var2;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GlyphPosition
    {
        public int XAdvance;
        public int YAdvance;
        public int XOffset;
        public int YOffset;
        private uint _var;
    }

    /// <summary>The HarfBuzz font of a face, its scale its units per em: positions come in
    /// font units. Made once, kept with the face's bytes for the process.</summary>
    private static nint FontFor(FontFace face)
    {
        lock (FontsLock)
        {
            if (Fonts.TryGetValue(face.Id, out nint cached)) return cached;

            byte[] data = face.File.Data;
            nint font;

            // the bytes are on the pinned heap: HarfBuzz may read them for as long as it likes
            fixed (byte* p = data)
            {
                nint blob = hb_blob_create(p, (uint)data.Length, MemoryModeReadOnly, 0, 0);
                nint hbFace = hb_face_create(blob, (uint)face.Index);

                font = hb_font_create(hbFace);
                hb_font_set_scale(font, face.UnitsPerEm, face.UnitsPerEm);

                // the font holds its face, the face its blob
                hb_face_destroy(hbFace);
                hb_blob_destroy(blob);
            }

            Fonts[face.Id] = font;
            return font;
        }
    }

    /// <summary>Shape a piece of a line in one face and one direction. The piece is added
    /// with the whole line as its context: the letters around a piece cut off by a change of
    /// font still decide the forms of the letters at its ends.</summary>
    /// <returns>The glyphs in visual order, their positions from the piece's left edge in
    /// font units (y up), and each glyph's cluster — the index of its first character in
    /// the line.</returns>
    public static (ushort[] Glyphs, int[] Advances, int[] OffsetsX, int[] OffsetsY, int[] Clusters) Shape(
        FontFace face, string text, int start, int length, bool rightToLeft)
    {
        nint font = FontFor(face);
        nint buffer = hb_buffer_create();

        try
        {
            fixed (char* chars = text)
                hb_buffer_add_utf16(buffer, chars, text.Length, (uint)start, length);

            hb_buffer_set_direction(buffer, rightToLeft ? DirectionRtl : DirectionLtr);
            hb_buffer_guess_segment_properties(buffer);

            hb_shape(font, buffer, 0, 0);

            uint count;
            GlyphInfo* infos = hb_buffer_get_glyph_infos(buffer, &count);
            GlyphPosition* positions = hb_buffer_get_glyph_positions(buffer, &count);

            var glyphs = new ushort[count];
            var advances = new int[count];
            var offsetsX = new int[count];
            var offsetsY = new int[count];
            var clusters = new int[count];

            for (int i = 0; i < count; i++)
            {
                glyphs[i] = (ushort)infos[i].Codepoint;
                clusters[i] = (int)infos[i].Cluster;
                advances[i] = positions[i].XAdvance;
                offsetsX[i] = positions[i].XOffset;
                offsetsY[i] = positions[i].YOffset;
            }

            return (glyphs, advances, offsetsX, offsetsY, clusters);
        }
        finally
        {
            hb_buffer_destroy(buffer);
        }
    }
}