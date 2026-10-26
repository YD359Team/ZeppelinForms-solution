using System.Buffers.Binary;

namespace ZeppelinForms.Gles.Imaging;

/// <summary>
/// GIF into premultiplied RGBA: the first frame, as Skia's decoder gives it for a still —
/// on the picture's logical screen, transparent around the frame and where its
/// transparent index is, interlaced or not.
/// </summary>
/// <remarks>The frames after the first are not decoded: <see cref="Drawing.Imaging.Image"/>
/// is a still.</remarks>
internal static class GifDecoder
{
    public static bool IsGif(ReadOnlySpan<byte> data) =>
        data.Length >= 6 && (data.StartsWith("GIF87a"u8) || data.StartsWith("GIF89a"u8));

    public static (int Width, int Height, byte[] Pixels) Decode(ReadOnlySpan<byte> data)
    {
        if (!IsGif(data) || data.Length < 13) throw new InvalidDataException("Not a GIF file.");

        int width = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
        int height = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
        int flags = data[10];

        int position = 13;
        ReadOnlySpan<byte> globalColors = default;

        if ((flags & 0x80) != 0)
        {
            int size = 3 << ((flags & 7) + 1);
            globalColors = Take(data, ref position, size);
        }

        int transparent = -1;

        while (position < data.Length)
        {
            byte block = data[position++];

            if (block == 0x3B) break; // the trailer: no frame at all

            if (block == 0x21)
            {
                // an extension: the graphic control one says which index is transparent
                byte label = Take(data, ref position, 1)[0];

                if (label == 0xF9 && position + 5 <= data.Length && data[position] == 4)
                {
                    if ((data[position + 1] & 1) != 0) transparent = data[position + 4];
                }

                SkipSubBlocks(data, ref position);
                continue;
            }

            if (block != 0x2C) throw new InvalidDataException("The GIF data has a block of an unknown kind.");

            ReadOnlySpan<byte> descriptor = Take(data, ref position, 9);

            int left = BinaryPrimitives.ReadUInt16LittleEndian(descriptor);
            int top = BinaryPrimitives.ReadUInt16LittleEndian(descriptor[2..]);
            int frameWidth = BinaryPrimitives.ReadUInt16LittleEndian(descriptor[4..]);
            int frameHeight = BinaryPrimitives.ReadUInt16LittleEndian(descriptor[6..]);
            int frameFlags = descriptor[8];

            ReadOnlySpan<byte> colors = globalColors;

            if ((frameFlags & 0x80) != 0)
            {
                int size = 3 << ((frameFlags & 7) + 1);
                colors = Take(data, ref position, size);
            }

            if (colors.IsEmpty) throw new InvalidDataException("The GIF frame has no colors.");

            // a screen of no size: the frame's own
            if (width == 0 || height == 0)
            {
                width = left + frameWidth;
                height = top + frameHeight;
            }

            int minimumCodeSize = Take(data, ref position, 1)[0];
            byte[] indices = Decompress(data, ref position, minimumCodeSize, frameWidth * frameHeight);

            return (width, height, Compose(width, height, left, top, frameWidth, frameHeight,
                (frameFlags & 0x40) != 0, indices, colors, transparent));
        }

        throw new InvalidDataException("The GIF file has no frame in it.");
    }

    private static ReadOnlySpan<byte> Take(ReadOnlySpan<byte> data, scoped ref int position, int count)
    {
        if (position + count > data.Length) throw new InvalidDataException("The GIF data is cut short.");

        ReadOnlySpan<byte> slice = data.Slice(position, count);
        position += count;
        return slice;
    }

    private static void SkipSubBlocks(ReadOnlySpan<byte> data, ref int position)
    {
        while (position < data.Length)
        {
            int size = data[position++];
            if (size == 0) return;

            position += size;
        }
    }

    /// <summary>The frame's color indices out of its LZW sub-blocks; a stream cut short
    /// leaves the rest as index 0, as browsers show a partly loaded GIF.</summary>
    private static byte[] Decompress(ReadOnlySpan<byte> data, ref int position, int minimumCodeSize, int count)
    {
        if (minimumCodeSize is < 1 or > 11) throw new InvalidDataException("The GIF frame's code size is out of range.");

        var output = new byte[count];
        int written = 0;

        int clear = 1 << minimumCodeSize;
        int end = clear + 1;

        // the dictionary as prefix links: a code's last byte and the code before it
        var prefix = new short[4096];
        var suffix = new byte[4096];
        var first = new byte[4096];
        var stack = new byte[4097];

        for (int i = 0; i < clear; i++)
        {
            suffix[i] = (byte)i;
            first[i] = (byte)i;
            prefix[i] = -1;
        }

        int codeSize = minimumCodeSize + 1;
        int next = end + 1;
        int previous = -1;

        int bits = 0, bitCount = 0;
        int blockLeft = 0;
        bool done = false;

        while (!done && written < count)
        {
            // enough bits for a code, from the sub-blocks
            while (bitCount < codeSize)
            {
                if (blockLeft == 0)
                {
                    if (position >= data.Length) { done = true; break; }

                    blockLeft = data[position++];
                    if (blockLeft == 0) { done = true; break; }
                }

                if (position >= data.Length) { done = true; break; }

                bits |= data[position++] << bitCount;
                bitCount += 8;
                blockLeft--;
            }

            if (done) break;

            int code = bits & ((1 << codeSize) - 1);
            bits >>= codeSize;
            bitCount -= codeSize;

            if (code == clear)
            {
                codeSize = minimumCodeSize + 1;
                next = end + 1;
                previous = -1;
                continue;
            }

            if (code == end) break;

            int top = 0;
            int current = code;

            if (code >= next)
            {
                // the code being defined right now: the previous string and its own first byte
                if (previous < 0 || code > next) break;

                stack[top++] = first[previous];
                current = previous;
            }

            while (current >= clear)
            {
                stack[top++] = suffix[current];
                current = prefix[current];

                if (top >= stack.Length - 1) throw new InvalidDataException("The GIF data has a broken dictionary.");
            }

            stack[top++] = suffix[current];

            byte firstByte = stack[top - 1];

            while (top > 0 && written < count) output[written++] = stack[--top];

            if (previous >= 0 && next < 4096)
            {
                prefix[next] = (short)previous;
                suffix[next] = firstByte;
                first[next] = first[previous];
                next++;

                if (next == 1 << codeSize && codeSize < 12) codeSize++;
            }

            previous = code;
        }

        // past the frame's remaining sub-blocks
        while (blockLeft > 0 && position < data.Length)
        {
            position += blockLeft;
            blockLeft = position < data.Length ? data[position++] : 0;
        }

        return output;
    }

    private static byte[] Compose(
        int width, int height, int left, int top, int frameWidth, int frameHeight,
        bool interlaced, byte[] indices, ReadOnlySpan<byte> colors, int transparent)
    {
        var pixels = new byte[width * height * 4];
        int colorCount = colors.Length / 3;

        for (int row = 0; row < frameHeight; row++)
        {
            int y = top + (interlaced ? InterlacedRow(row, frameHeight) : row);
            if (y >= height) continue;

            for (int column = 0; column < frameWidth; column++)
            {
                int x = left + column;
                if (x >= width) continue;

                int index = indices[row * frameWidth + column];
                if (index == transparent || index >= colorCount) continue;

                int o = (y * width + x) * 4;
                pixels[o] = colors[index * 3];
                pixels[o + 1] = colors[index * 3 + 1];
                pixels[o + 2] = colors[index * 3 + 2];
                pixels[o + 3] = 255;
            }
        }

        return pixels;
    }

    /// <summary>Where the n-th row of an interlaced frame goes: every eighth row from 0,
    /// every eighth from 4, every fourth from 2, every second from 1.</summary>
    private static int InterlacedRow(int n, int height)
    {
        int pass1 = (height + 7) / 8;
        if (n < pass1) return n * 8;
        n -= pass1;

        int pass2 = (height + 3) / 8;
        if (n < pass2) return 4 + n * 8;
        n -= pass2;

        int pass3 = (height + 1) / 4;
        if (n < pass3) return 2 + n * 4;
        n -= pass3;

        return 1 + n * 2;
    }
}