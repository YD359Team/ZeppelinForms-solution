using ZeppelinForms.Drawing.Imaging;

namespace ZeppelinForms.Gles.Imaging;

/// <summary>Pictures decoded in managed code, by their signature: PNG and BMP. What the
/// GLES backend has instead of Skia's codecs, with no native library behind it.</summary>
/// <remarks>JPEG, GIF and WebP are recognized and refused with a message that says so:
/// the preview of 0.18 doesn't decode them yet.</remarks>
public sealed class GlesImageDecoder : ImageDecoder
{
    public static void Register() => Current = new GlesImageDecoder();

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
            _ when BmpDecoder.IsBmp(bytes) => BmpDecoder.Decode(bytes),
            _ => throw new InvalidDataException(Unsupported(bytes)),
        };

        return new Image(width, height, pixels);
    }

    private static string Unsupported(ReadOnlySpan<byte> data)
    {
        string? format =
            data.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]) ? "JPEG" :
            data.StartsWith("GIF8"u8) ? "GIF" :
            data.Length >= 12 && data.StartsWith("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8) ? "WebP" :
            null;

        return format is null
            ? "The picture is in a format the GLES backend doesn't decode: PNG and BMP are supported."
            : $"{format} pictures are not decoded by the GLES backend yet: PNG and BMP are supported.";
    }
}