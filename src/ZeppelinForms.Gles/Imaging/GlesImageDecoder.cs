using ZeppelinForms.Drawing.Imaging;

namespace ZeppelinForms.Gles.Imaging;

/// <summary>Pictures decoded in managed code, by their signature: PNG, JPEG, GIF and BMP. What
/// the GLES backend has instead of Skia's codecs, with no native library behind it.</summary>
/// <remarks>
/// <para>
/// A picture larger than <see cref="MaxDimension"/> either way is scaled down to fit it,
/// keeping its proportions, by averaging — as Skia's decoder does, to the same default.
/// </para>
/// <para>
/// WebP is recognized and refused with a message that says so; of a GIF, the first frame
/// is decoded, as Skia's decoder gives it.
/// </para>
/// </remarks>
public sealed class GlesImageDecoder : ImageDecoder
{
    /// <summary>The largest width and height a picture is kept at: a desktop application
    /// has no reason to hold more pixels than fit on the screen.</summary>
    public int MaxDimension { get; set; } = 2048;

    public static void Register() => Current = new GlesImageDecoder();

    /// <summary>Register with a resolution ceiling of your own: an application showing
    /// icon-sized pictures needn't keep them at 2048 × 2048, 16 MB of pixels each.</summary>
    public static void Register(int maxDimension) =>
        Current = new GlesImageDecoder { MaxDimension = maxDimension };

    public override Image Decode(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        byte[] data;

        if (stream is MemoryStream memory && memory.TryGetBuffer(out ArraySegment<byte> buffer))
        {
            data = buffer.AsSpan((int)memory.Position).ToArray();
        }
        else
        {
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            data = copy.ToArray();
        }

        ReadOnlySpan<byte> bytes = data;

        (int width, int height, byte[] pixels) = true switch
        {
            _ when PngDecoder.IsPng(bytes) => PngDecoder.Decode(bytes),
            _ when JpegDecoder.IsJpeg(bytes) => JpegDecoder.Decode(data, MaxDimension),
            _ when GifDecoder.IsGif(bytes) => GifDecoder.Decode(bytes),
            _ when BmpDecoder.IsBmp(bytes) => BmpDecoder.Decode(bytes),
            _ => throw new InvalidDataException(Unsupported(bytes)),
        };

        if (MaxDimension > 0 && (width > MaxDimension || height > MaxDimension))
            (width, height, pixels) = Downscale(width, height, pixels, MaxDimension);

        return new Image(width, height, pixels);
    }

    /// <summary>Premultiplied RGBA scaled down to fit a square of the size given: every
    /// pixel the average of the source's area under it, partial pixels at its edges
    /// counted by their share — across first, then down.</summary>
    internal static (int Width, int Height, byte[] Pixels) Downscale(int width, int height, byte[] pixels, int limit)
    {
        double scale = Math.Min((double)limit / width, (double)limit / height);

        int newWidth = Math.Max(1, (int)Math.Round(width * scale));
        int newHeight = Math.Max(1, (int)Math.Round(height * scale));

        (int[] columnStarts, float[][] columnWeights) = Weights(width, newWidth);
        (int[] rowStarts, float[][] rowWeights) = Weights(height, newHeight);

        // across: every source row into a row of the new width
        var across = new float[newWidth * height * 4];

        for (int y = 0; y < height; y++)
        {
            int source = y * width * 4;
            int target = y * newWidth * 4;

            for (int ox = 0; ox < newWidth; ox++)
            {
                float r = 0, g = 0, b = 0, a = 0;
                float[] weights = columnWeights[ox];
                int s = source + columnStarts[ox] * 4;

                for (int k = 0; k < weights.Length; k++, s += 4)
                {
                    float w = weights[k];
                    r += pixels[s] * w;
                    g += pixels[s + 1] * w;
                    b += pixels[s + 2] * w;
                    a += pixels[s + 3] * w;
                }

                int t = target + ox * 4;
                across[t] = r;
                across[t + 1] = g;
                across[t + 2] = b;
                across[t + 3] = a;
            }
        }

        // down: the rows of a new row summed
        var output = new byte[newWidth * newHeight * 4];
        int rowLength = newWidth * 4;

        for (int oy = 0; oy < newHeight; oy++)
        {
            float[] weights = rowWeights[oy];
            int target = oy * rowLength;

            for (int i = 0; i < rowLength; i++)
            {
                float sum = 0;
                int s = rowStarts[oy] * rowLength + i;

                for (int k = 0; k < weights.Length; k++, s += rowLength)
                    sum += across[s] * weights[k];

                output[target + i] = (byte)Math.Clamp((int)(sum + 0.5f), 0, 255);
            }
        }

        return (newWidth, newHeight, output);
    }

    /// <summary>For every new pixel along one axis, the first source pixel under it and the
    /// shares of the source pixels it covers, summing to one.</summary>
    private static (int[] Starts, float[][] Weights) Weights(int size, int newSize)
    {
        double step = (double)size / newSize;
        var starts = new int[newSize];
        var weights = new float[newSize][];

        for (int o = 0; o < newSize; o++)
        {
            double from = o * step, to = Math.Min(size, from + step);
            int first = (int)from;
            int last = Math.Min(size - 1, (int)Math.Ceiling(to) - 1);

            var w = new float[last - first + 1];

            for (int sx = first; sx <= last; sx++)
                w[sx - first] = (float)((Math.Min(to, sx + 1) - Math.Max(from, sx)) / step);

            starts[o] = first;
            weights[o] = w;
        }

        return (starts, weights);
    }

    private static string Unsupported(ReadOnlySpan<byte> data)
    {
        bool webp = data.Length >= 12 && data.StartsWith("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8);

        return webp
            ? "WebP pictures are not decoded by the GLES backend yet: PNG, JPEG, GIF and BMP are supported."
            : "The picture is in a format the GLES backend doesn't decode: PNG, JPEG, GIF and BMP are supported.";
    }
}