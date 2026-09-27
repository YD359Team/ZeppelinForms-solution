using System.Buffers.Binary;

namespace ZeppelinForms.Drawing.Imaging;

public sealed class Icon
{
    private readonly byte[] _data;
    private readonly ImageEntry[] _images;

    private Icon(byte[] data, ImageEntry[] images)
    {
        _data = data;
        _images = images;
    }

    public static Icon FromStream(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);

        byte[] data = ms.ToArray();

        if (data.Length < 6)
            throw new InvalidDataException("Invalid ICO file.");

        ushort reserved = BinaryPrimitives.ReadUInt16LittleEndian(
            data.AsSpan(0, 2));

        ushort type = BinaryPrimitives.ReadUInt16LittleEndian(
            data.AsSpan(2, 2));

        ushort count = BinaryPrimitives.ReadUInt16LittleEndian(
            data.AsSpan(4, 2));

        if (reserved != 0 || type != 1 || count == 0)
            throw new InvalidDataException("Invalid ICO file.");

        int directorySize = 6 + count * 16;

        if (data.Length < directorySize)
            throw new InvalidDataException("Invalid ICO file.");

        ImageEntry[] images = new ImageEntry[count];

        for (int i = 0; i < count; i++)
        {
            int offset = 6 + i * 16;

            int width = data[offset];
            int height = data[offset + 1];

            // in ICO the value 0 means 256.
            if (width == 0)
                width = 256;

            if (height == 0)
                height = 256;

            int colorCount = data[offset + 2];

            ushort planes = BinaryPrimitives.ReadUInt16LittleEndian(
                data.AsSpan(offset + 4, 2));

            ushort bitCount = BinaryPrimitives.ReadUInt16LittleEndian(
                data.AsSpan(offset + 6, 2));

            uint size = BinaryPrimitives.ReadUInt32LittleEndian(
                data.AsSpan(offset + 8, 4));

            uint imageOffset = BinaryPrimitives.ReadUInt32LittleEndian(
                data.AsSpan(offset + 12, 4));

            if (imageOffset > data.Length ||
                size > data.Length - imageOffset)
            {
                throw new InvalidDataException(
                    "Invalid ICO image data.");
            }

            images[i] = new ImageEntry(
                width,
                height,
                colorCount,
                planes,
                bitCount,
                (int)imageOffset,
                (int)size);
        }

        return new Icon(data, images);
    }

    public static Icon FromFile(string path)
    {
        using FileStream stream = File.OpenRead(path);

        return FromStream(stream);
    }

    public ReadOnlySpan<byte> GetImage(
        int requestedWidth,
        int requestedHeight)
    {
        ImageEntry image = SelectImage(
            requestedWidth,
            requestedHeight);

        return _data.AsSpan(
            image.Offset,
            image.Size);
    }

    /// <summary>The image of the required size as a standalone file
    /// suitable for a decoder.</summary>
    /// <remarks>
    /// Inside an ICO an image is stored in one of two ways: a whole PNG
    /// or a DIB — a BMP without the 14-byte file header, which isn't needed
    /// in the container. Decoders don't open a standalone DIB, so the header
    /// has to be restored.
    /// </remarks>
    public byte[] GetImageFile(int requestedWidth = 256, int requestedHeight = 256)
    {
        ReadOnlySpan<byte> raw = GetImage(requestedWidth, requestedHeight);

        // a PNG inside an ICO lies whole and needs no repacking
        if (raw.Length >= 8 &&
            raw[0] == 0x89 && raw[1] == 0x50 && raw[2] == 0x4E && raw[3] == 0x47)
        {
            return raw.ToArray();
        }

        return WrapDib(raw);
    }

    /// <summary>An image ready for drawing.</summary>
    public Image ToImage(int requestedWidth = 256, int requestedHeight = 256)
    {
        using var stream = new MemoryStream(GetImageFile(requestedWidth, requestedHeight));
        return Image.Load(stream);
    }

    /// <summary>Prepend the BMP file header to a DIB.</summary>
    private static byte[] WrapDib(ReadOnlySpan<byte> dib)
    {
        const int FileHeaderSize = 14;

        if (dib.Length < 4)
            throw new InvalidDataException("The image in the ICO is too short.");

        int headerSize = BinaryPrimitives.ReadInt32LittleEndian(dib);

        // in ICO the height in the header is doubled: the DIB describes the picture
        // together with the transparency mask that follows it. For a BMP this must
        // be fixed, otherwise the decoder reads the mask as the lower half of the image.
        int height = dib.Length >= 12
            ? BinaryPrimitives.ReadInt32LittleEndian(dib[8..])
            : 0;

        int bitCount = dib.Length >= 16
            ? BinaryPrimitives.ReadUInt16LittleEndian(dib[14..])
            : 32;

        // biClrUsed: how many palette entries are actually stored. Zero means
        // the full palette for the bit depth. Editors save a shortened palette
        // to save space, and counting 2^bitCount entries regardless put
        // the pixel offset past the real start of the pixels
        int colorsUsed = headerSize >= 36 && dib.Length >= 36
            ? BinaryPrimitives.ReadInt32LittleEndian(dib[32..])
            : 0;

        byte[] file = new byte[FileHeaderSize + dib.Length];

        file[0] = (byte)'B';
        file[1] = (byte)'M';

        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(2), file.Length);
        // 4 bytes are reserved and stay zero

        // the palette lies between the header and the pixels; 24- and 32-bit images have none
        int paletteEntries = bitCount <= 8
            ? (colorsUsed > 0 ? colorsUsed : 1 << bitCount)
            : 0;

        int paletteSize = paletteEntries * 4;

        BinaryPrimitives.WriteInt32LittleEndian(
            file.AsSpan(10), FileHeaderSize + headerSize + paletteSize);

        dib.CopyTo(file.AsSpan(FileHeaderSize));

        if (height != 0)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                file.AsSpan(FileHeaderSize + 8), height / 2);
        }

        return file;
    }

    /// <summary>The ICO content as a whole. Needed where the icon is taken not by
    /// the system but by something else — a page's favicon, for example: the browser
    /// wants the whole file, not a single image from it.</summary>
    public ReadOnlySpan<byte> GetRawData() => _data;

    private ImageEntry SelectImage(
        int requestedWidth,
        int requestedHeight)
    {
        ImageEntry best = _images[0];

        int bestScore = int.MaxValue;

        foreach (ImageEntry image in _images)
        {
            int widthDifference =
                Math.Abs(image.Width - requestedWidth);

            int heightDifference =
                Math.Abs(image.Height - requestedHeight);

            int score =
                widthDifference * widthDifference +
                heightDifference * heightDifference;

            // with the same size, prefer the image
            // with the greater color depth.
            if (score == bestScore &&
                image.BitCount > best.BitCount)
            {
                best = image;
                continue;
            }

            if (score < bestScore)
            {
                best = image;
                bestScore = score;
            }
        }

        return best;
    }

    private readonly struct ImageEntry
    {
        public readonly int Width;
        public readonly int Height;
        public readonly int ColorCount;
        public readonly ushort Planes;
        public readonly ushort BitCount;
        public readonly int Offset;
        public readonly int Size;

        public ImageEntry(
            int width,
            int height,
            int colorCount,
            ushort planes,
            ushort bitCount,
            int offset,
            int size)
        {
            Width = width;
            Height = height;
            ColorCount = colorCount;
            Planes = planes;
            BitCount = bitCount;
            Offset = offset;
            Size = size;
        }
    }
}