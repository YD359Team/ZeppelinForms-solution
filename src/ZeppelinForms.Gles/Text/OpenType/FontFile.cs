using System.Buffers.Binary;

namespace ZeppelinForms.Gles.Text.OpenType;

/// <summary>
/// The bytes of a font file — one face, or a collection of them (.ttc, .otc) — read once
/// and shared by every face in it and every size drawn from it.
/// </summary>
/// <remarks>
/// <para>
/// The array is allocated on the pinned object heap: HarfBuzz reads the tables straight
/// from it for as long as the process lives, so its address must never move, and a pinned
/// handle on an ordinary array would fragment the heap it lives in.
/// </para>
/// <para>
/// Files are kept by their full path for the life of the process, as Skia keeps its
/// typefaces: an application uses a handful of fonts, and a collection of CJK faces read
/// twice would cost twice its twenty megabytes.
/// </para>
/// </remarks>
internal sealed class FontFile
{
    private static readonly Dictionary<string, FontFile> Files = new(StringComparer.Ordinal);
    private static readonly Lock FilesLock = new();

    private readonly int[] _faceOffsets;
    private readonly FontFace?[] _faces;
    private readonly bool[] _facesRead;

    private FontFile(string name, byte[] data, int[] faceOffsets)
    {
        Name = name;
        Data = data;
        _faceOffsets = faceOffsets;
        _faces = new FontFace?[faceOffsets.Length];
        _facesRead = new bool[faceOffsets.Length];
    }

    /// <summary>A face of the file, read once: the same object every time, so typefaces
    /// and the caches keyed by them see one face. Null — the face can't be drawn with.</summary>
    public FontFace? Face(int index)
    {
        if (index < 0 || index >= _faces.Length) return null;

        lock (_faces)
        {
            if (!_facesRead[index])
            {
                _faces[index] = FontFace.TryOpen(this, index);
                _facesRead[index] = true;
            }

            return _faces[index];
        }
    }

    /// <summary>The path it was read from, or the name it was given in memory.</summary>
    public string Name { get; }

    /// <summary>The whole file, at an address that never moves.</summary>
    public byte[] Data { get; }

    public int FaceCount => _faceOffsets.Length;

    /// <summary>Where the table directory of a face begins.</summary>
    public int FaceOffset(int index) => _faceOffsets[index];

    /// <summary>A file read once and kept: the same object for every request of the path.</summary>
    /// <exception cref="InvalidDataException">The file is no font this reader knows.</exception>
    public static FontFile Load(string path)
    {
        string full = Path.GetFullPath(path);

        lock (FilesLock)
        {
            if (Files.TryGetValue(full, out FontFile? cached)) return cached;
        }

        byte[] bytes;

        using (FileStream stream = File.OpenRead(full))
        {
            long length = stream.Length;

            if (length > int.MaxValue) throw new InvalidDataException($"The font file is too large: {full}.");

            bytes = GC.AllocateUninitializedArray<byte>((int)length, pinned: true);
            stream.ReadExactly(bytes);
        }

        FontFile file = FromPinned(bytes, full);

        lock (FilesLock)
        {
            // two threads read the same file at once: the first one stays
            if (Files.TryGetValue(full, out FontFile? raced)) return raced;

            Files[full] = file;
        }

        return file;
    }

    /// <summary>A font in memory: an embedded resource, a download. The bytes are copied
    /// to the pinned heap: the caller's array may move, and may be changed.</summary>
    public static FontFile FromBytes(ReadOnlySpan<byte> bytes, string name)
    {
        byte[] pinned = GC.AllocateUninitializedArray<byte>(bytes.Length, pinned: true);
        bytes.CopyTo(pinned);

        return FromPinned(pinned, name);
    }

    private static FontFile FromPinned(byte[] pinned, string name) =>
        new(name, pinned, ReadFaceOffsets(pinned, name));

    private static int[] ReadFaceOffsets(ReadOnlySpan<byte> data, string name)
    {
        if (data.Length < 12) throw new InvalidDataException($"Not a font file: {name}.");

        uint tag = BinaryPrimitives.ReadUInt32BigEndian(data);

        // 'ttcf': a collection, its faces' directories listed after the header
        if (tag == 0x74746366)
        {
            int count = (int)BinaryPrimitives.ReadUInt32BigEndian(data[8..]);

            if (count <= 0 || 12 + count * 4 > data.Length)
                throw new InvalidDataException($"A font collection with a broken header: {name}.");

            int[] offsets = new int[count];

            for (int i = 0; i < count; i++)
                offsets[i] = (int)BinaryPrimitives.ReadUInt32BigEndian(data[(12 + i * 4)..]);

            return offsets;
        }

        // TrueType outlines (1.0 or 'true'), CFF outlines ('OTTO')
        if (tag is 0x00010000 or 0x74727565 or 0x4F54544F)
            return [0];

        throw new InvalidDataException($"Not a font file this reader knows (WOFF and Type 1 are not): {name}.");
    }
}