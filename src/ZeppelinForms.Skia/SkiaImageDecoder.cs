using SkiaSharp;
using ZeppelinForms.Drawing.Imaging;

namespace ZeppelinForms.Skia;

public sealed class SkiaImageDecoder : ImageDecoder
{
    public static void Register() => Current = new SkiaImageDecoder();

    public override Image Decode(Stream stream)
    {
        using SKBitmap decoded = SKBitmap.Decode(stream)
            ?? throw new InvalidDataException("Не удалось декодировать изображение.");

        // приводим к единому формату, с которым работает весь остальной рендер-пайплайн
        using SKBitmap normalized = decoded.ColorType == SKColorType.Rgba8888
            ? decoded
            : decoded.Copy(SKColorType.Rgba8888);

        // Bytes отдаёт копию управляемого массива — можно спокойно хранить в Image
        return new Image(normalized.Width, normalized.Height, normalized.Bytes);
    }
}
