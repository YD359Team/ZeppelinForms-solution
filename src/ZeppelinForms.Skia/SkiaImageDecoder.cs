using SkiaSharp;
using ZeppelinForms.Drawing.Imaging;

namespace ZeppelinForms.Skia;

/// <summary>Decodes images into <see cref="Image"/>: RGBA, 8 bits per channel,
/// premultiplied alpha.</summary>
/// <remarks>
/// Premultiplied is the contract, not a detail: everyone who uploads an Image into
/// Skia — SkiaGraphics, SkiaOffscreenRenderer, every platform surface — declares it
/// as SKAlphaType.Premul. It holds because SKBitmap.Decode(SKCodec) turns an
/// Unpremul codec into a Premul bitmap by itself. A decoder that produced unpremul
/// pixels would break nothing loudly: semi-transparent edges would just come out
/// darker.
/// </remarks>
public sealed class SkiaImageDecoder : ImageDecoder
{
    // A reasonable ceiling: a desktop application has no reason to keep in memory
    // a resolution higher than what physically fits on the screen.
    public int MaxDimension { get; set; } = 2048;

    public static void Register() => Current = new SkiaImageDecoder();

    /// <summary>Register with a resolution ceiling of your own. An application that
    /// shows icon-sized pictures has no reason to keep them at full resolution:
    /// 2048×2048 is 16 MB of pixels regardless of the control's size.</summary>
    public static void Register(int maxDimension) =>
        Current = new SkiaImageDecoder { MaxDimension = maxDimension };

    /// <remarks>
    /// Every intermediate copy is a full pixel buffer: 16 MB per copy for
    /// a 2048×2048 image. So we copy exactly as many times as needed: downscaling
    /// if it doesn't fit under the ceiling, a format change if it isn't Rgba8888,
    /// and one transfer into the managed array that Image takes. The previous code
    /// did Copy unconditionally and one more Copy for the format, that is, it held
    /// four buffers in memory instead of two.
    /// </remarks>
    public override Image Decode(Stream stream)
    {
        // SKBitmap.Decode(Stream) returns null on failure and keeps quiet about the
        // reason. Through SKCodec the error code is visible, and the signature in the
        // message answers the main question right away: is this a picture at all?
        // A file downloaded from a path that doesn't exist on the server often turns
        // out to be an error page, and it starts with "<!DO"
        long length = stream.CanSeek ? stream.Length : -1;
        string signature = ReadSignature(stream);

        using SKCodec? codec = SKCodec.Create(stream, out SKCodecResult result);

        if (codec is null)
            throw new InvalidDataException(
                $"Could not decode the image: {result}, " +
                $"{length} bytes, signature {signature}.");

        using SKBitmap decoded = SKBitmap.Decode(codec)
            ?? throw new InvalidDataException(
                $"The format was recognized as {codec.EncodedFormat}, " +
                $"but the pixels could not be read ({length} bytes).");

        SKBitmap? resized = null;
        SKBitmap? converted = null;

        try
        {
            SKBitmap source = decoded;

            if (source.Width > MaxDimension || source.Height > MaxDimension)
            {
                resized = Downscale(source, MaxDimension);
                source = resized;
            }

            if (source.ColorType != SKColorType.Rgba8888)
            {
                converted = source.Copy(SKColorType.Rgba8888)
                    ?? throw new InvalidDataException("Could not convert the image to Rgba8888.");

                source = converted;
            }

            // Bytes makes a managed copy itself — that is what goes into Image,
            // and all native buffers are released right here
            return new Image(source.Width, source.Height, source.Bytes);
        }
        finally
        {
            resized?.Dispose();
            converted?.Dispose();
        }
    }

    /// <summary>The first bytes as "41 42 43 44 (ABCD)" — they show whether
    /// something other than a picture was passed in.</summary>
    private static string ReadSignature(Stream stream)
    {
        if (!stream.CanSeek) return "unknown";

        long position = stream.Position;

        try
        {
            Span<byte> head = stackalloc byte[4];
            int read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);

            if (read == 0) return "empty";

            head = head[..read];

            var hex = new System.Text.StringBuilder();
            var ascii = new System.Text.StringBuilder();

            foreach (byte value in head)
            {
                hex.Append(hex.Length > 0 ? " " : string.Empty).Append(value.ToString("X2"));
                ascii.Append(value is >= 0x20 and < 0x7F ? (char)value : '.');
            }

            return $"{hex} ({ascii})";
        }
        finally
        {
            stream.Position = position;
        }
    }

    private static SKBitmap Downscale(SKBitmap source, int maxDimension)
    {
        float scale = maxDimension / (float)Math.Max(source.Width, source.Height);
        int width = Math.Max(1, (int)(source.Width * scale));
        int height = Math.Max(1, (int)(source.Height * scale));

        return source.Resize(new SKSizeI(width, height), SKSamplingOptions.Default)
            ?? throw new InvalidDataException("Could not downscale the image.");
    }
}