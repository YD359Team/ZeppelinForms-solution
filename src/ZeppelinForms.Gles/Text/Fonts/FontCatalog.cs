using System.Buffers.Binary;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ZeppelinForms.Gles.Text.Fonts;

/// <summary>
/// The fonts installed on the machine, found by walking the system's font folders and
/// reading each face's names and style: what fonts are matched by where there is no
/// fontconfig — Windows, macOS, Android, a bare Linux.
/// </summary>
/// <remarks>
/// <para>
/// Only the table directory and the name, OS/2 and head tables are read of each file, a
/// few kilobytes from its start: a collection of CJK faces is twenty megabytes, and the
/// walk must not read it. The whole file is read later, if a face of it is drawn.
/// </para>
/// <para>
/// The walk runs once, on the first font asked for, and its result is kept for the
/// process: fonts installed while it runs are seen after a restart, as in most toolkits.
/// </para>
/// </remarks>
internal static class FontCatalog
{
    private static readonly Lazy<IReadOnlyList<CatalogEntry>> Lazy = new(() => Scan(), LazyThreadSafetyMode.ExecutionAndPublication);

    public static IReadOnlyList<CatalogEntry> Entries => Lazy.Value;

    /// <summary>The folders fonts are installed in, the user's own last.</summary>
    internal static IEnumerable<string> Folders()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (OperatingSystem.IsWindows())
        {
            yield return Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            yield return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts");
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return "/System/Library/Fonts";
            yield return "/Library/Fonts";
            yield return System.IO.Path.Combine(home, "Library", "Fonts");
        }
        else if (OperatingSystem.IsAndroid())
        {
            yield return "/system/fonts";
            yield return "/product/fonts";
        }
        else
        {
            yield return "/usr/share/fonts";
            yield return "/usr/local/share/fonts";

            string dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } xdg
                ? xdg
                : System.IO.Path.Combine(home, ".local", "share");

            yield return System.IO.Path.Combine(dataHome, "fonts");
            yield return System.IO.Path.Combine(home, ".fonts");
        }
    }

    private static List<CatalogEntry> Scan()
    {
        var entries = new List<CatalogEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (string folder in Folders())
        {
            if (!Directory.Exists(folder)) continue;

            IEnumerable<string> files;

            try
            {
                files = Directory.EnumerateFiles(folder, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = 0,
                });
            }
            catch (IOException)
            {
                continue;
            }

            foreach (string file in files)
            {
                string extension = System.IO.Path.GetExtension(file);

                if (!extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase) &&
                    !extension.Equals(".otf", StringComparison.OrdinalIgnoreCase) &&
                    !extension.Equals(".ttc", StringComparison.OrdinalIgnoreCase) &&
                    !extension.Equals(".otc", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!seen.Add(file)) continue;

                try
                {
                    ReadFile(file, entries);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException or InvalidDataException)
                {
                    // a broken or unreadable file is no font of the catalog
                }
            }
        }

        return entries;
    }

    private static void ReadFile(string path, List<CatalogEntry> entries)
    {
        using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        Span<byte> header = stackalloc byte[12];
        if (RandomAccess.Read(handle, header, 0) < 12) return;

        uint tag = BinaryPrimitives.ReadUInt32BigEndian(header);

        if (tag == 0x74746366)
        {
            int count = (int)BinaryPrimitives.ReadUInt32BigEndian(header[8..]);
            if (count is <= 0 or > 1000) return;

            byte[] offsets = new byte[count * 4];
            RandomAccess.Read(handle, offsets, 12);

            for (int i = 0; i < count; i++)
            {
                long offset = BinaryPrimitives.ReadUInt32BigEndian(offsets.AsSpan(i * 4));
                if (ReadFace(handle, path, i, offset) is { } entry) entries.Add(entry);
            }
        }
        else if (tag is 0x00010000 or 0x74727565 or 0x4F54544F)
        {
            if (ReadFace(handle, path, 0, 0) is { } entry) entries.Add(entry);
        }
    }

    private static CatalogEntry? ReadFace(SafeFileHandle handle, string path, int index, long directory)
    {
        Span<byte> top = stackalloc byte[12];
        if (RandomAccess.Read(handle, top, directory) < 12) return null;

        int numTables = BinaryPrimitives.ReadUInt16BigEndian(top[4..]);
        if (numTables is 0 or > 200) return null;

        byte[] records = new byte[numTables * 16];
        if (RandomAccess.Read(handle, records, directory + 12) < records.Length) return null;

        (long Offset, int Length) name = default, os2 = default, head = default, cmap = default;
        bool outlines = false, color = false;

        for (int i = 0; i < numTables; i++)
        {
            ReadOnlySpan<byte> record = records.AsSpan(i * 16, 16);

            uint tableTag = BinaryPrimitives.ReadUInt32BigEndian(record);
            long offset = BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(record[12..]);

            switch (tableTag)
            {
                case 0x6E616D65: name = (offset, length); break;            // name
                case 0x4F532F32: os2 = (offset, length); break;             // OS/2
                case 0x68656164: head = (offset, length); break;            // head
                case 0x636D6170: cmap = (offset, length); break;            // cmap
                case 0x676C7966 or 0x43464620: outlines = true; break;      // glyf, CFF
                case 0x43424454: color = true; break;                       // CBDT
            }
        }

        // a face of nothing to draw with — a bitmap-only font of another kind, CFF2
        if (!outlines && !color) return null;
        if (name.Length <= 0) return null;

        byte[] names = new byte[Math.Min(name.Length, 1 << 20)];
        RandomAccess.Read(handle, names, name.Offset);

        List<string> families = ReadFamilies(names);
        if (families.Count == 0) return null;

        int weight = 400;
        int width = 5;
        bool italic = false;

        if (os2.Length >= 64)
        {
            byte[] table = new byte[64];
            RandomAccess.Read(handle, table, os2.Offset);

            weight = BinaryPrimitives.ReadUInt16BigEndian(table.AsSpan(4));
            if (weight is > 0 and < 10) weight *= 100;

            width = BinaryPrimitives.ReadUInt16BigEndian(table.AsSpan(6));

            italic = (BinaryPrimitives.ReadUInt16BigEndian(table.AsSpan(62)) & 0x0201) != 0;
        }
        else if (head.Length >= 46)
        {
            byte[] table = new byte[46];
            RandomAccess.Read(handle, table, head.Offset);

            int macStyle = BinaryPrimitives.ReadUInt16BigEndian(table.AsSpan(44));
            weight = (macStyle & 1) != 0 ? 700 : 400;
            italic = (macStyle & 2) != 0;
        }

        return new CatalogEntry(path, index, families, weight, width, italic, color) { CmapOffset = cmap.Offset, CmapLength = cmap.Length };
    }

    /// <summary>The family names of the name table: the typographic ones (16) first, in
    /// every language, then the legacy ones (1).</summary>
    internal static List<string> ReadFamilies(ReadOnlySpan<byte> name)
    {
        var typographic = new List<string>();
        var legacy = new List<string>();

        if (name.Length < 6) return typographic;

        int count = BinaryPrimitives.ReadUInt16BigEndian(name[2..]);
        int storage = BinaryPrimitives.ReadUInt16BigEndian(name[4..]);

        for (int i = 0; i < count; i++)
        {
            int record = 6 + i * 12;
            if (record + 12 > name.Length) break;

            int platform = BinaryPrimitives.ReadUInt16BigEndian(name[record..]);
            int encoding = BinaryPrimitives.ReadUInt16BigEndian(name[(record + 2)..]);
            int nameId = BinaryPrimitives.ReadUInt16BigEndian(name[(record + 6)..]);
            int length = BinaryPrimitives.ReadUInt16BigEndian(name[(record + 8)..]);
            int offset = BinaryPrimitives.ReadUInt16BigEndian(name[(record + 10)..]);

            if (nameId is not (1 or 16)) continue;
            if (storage + offset + length > name.Length) continue;

            ReadOnlySpan<byte> bytes = name.Slice(storage + offset, length);

            string? text = platform switch
            {
                0 or 3 when encoding is 0 or 1 or 10 => Encoding.BigEndianUnicode.GetString(bytes),
                1 when encoding == 0 => Encoding.Latin1.GetString(bytes),
                _ => null,
            };

            if (string.IsNullOrWhiteSpace(text)) continue;

            List<string> list = nameId == 16 ? typographic : legacy;
            text = text.Trim();

            if (!list.Contains(text, StringComparer.OrdinalIgnoreCase)) list.Add(text);
        }

        foreach (string family in legacy)
            if (!typographic.Contains(family, StringComparer.OrdinalIgnoreCase))
                typographic.Add(family);

        return typographic;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CatalogEntry, OpenType.CharacterMap> Maps = [];

    /// <summary>Whether a face has a glyph for the character, by its character map alone:
    /// the walk for a fallback reads a few kilobytes of each file, not the whole file.</summary>
    public static bool Covers(CatalogEntry entry, int codepoint)
    {
        OpenType.CharacterMap map = Maps.GetValue(entry, static e =>
        {
            if (e.CmapLength <= 0) return OpenType.CharacterMap.None;

            try
            {
                using SafeFileHandle handle = File.OpenHandle(e.Path, FileMode.Open, FileAccess.Read, FileShare.Read);

                byte[] table = new byte[e.CmapLength];
                int read = RandomAccess.Read(handle, table, e.CmapOffset);

                return read == table.Length ? OpenType.CharacterMap.Read(table, 0, table.Length) : OpenType.CharacterMap.None;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return OpenType.CharacterMap.None;
            }
        });

        return map.GlyphFor(codepoint) != 0;
    }

    /// <summary>The faces of a family, by any of its names.</summary>
    public static IEnumerable<CatalogEntry> Family(string family) =>
        Entries.Where(e => e.Families.Contains(family, StringComparer.OrdinalIgnoreCase));

    /// <summary>The face of a family nearest a weight and a slant, as CSS matches them: the
    /// slant first, the normal width next, then the nearest weight — a lighter one for a
    /// light request, a heavier one for a heavy request, when the exact one is missing.</summary>
    public static CatalogEntry? Best(IEnumerable<CatalogEntry> faces, int weight, bool italic)
    {
        CatalogEntry? best = null;
        int bestScore = int.MaxValue;

        foreach (CatalogEntry face in faces)
        {
            int score = face.Italic == italic ? 0 : 10000;

            // a condensed or expanded face only where the family has no normal one
            score += Math.Abs(face.Width - 5) * 1000;

            int distance = Math.Abs(face.Weight - weight);

            // CSS: under 400 lighter is preferred, over 500 heavier; a step the wrong way costs more
            bool wrongWay = weight >= 500 ? face.Weight < weight : face.Weight > weight;
            score += distance * (wrongWay ? 3 : 1);

            if (score < bestScore)
            {
                bestScore = score;
                best = face;
            }
        }

        return best;
    }
}

/// <summary>A face the catalog found: its file and index, its family names and its style.</summary>
/// <param name="Width">OpenType's width class: 5 normal, below condensed, above expanded.</param>
internal sealed record CatalogEntry(string Path, int Index, IReadOnlyList<string> Families, int Weight, int Width, bool Italic, bool HasColor)
{
    public string Family => Families[0];

    /// <summary>Where the face's character map is in its file.</summary>
    public long CmapOffset { get; init; }

    public int CmapLength { get; init; }
}