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

            // В ICO значение 0 означает 256.
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

            // При одинаковом размере предпочитаем
            // изображение с большей глубиной цвета.
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
