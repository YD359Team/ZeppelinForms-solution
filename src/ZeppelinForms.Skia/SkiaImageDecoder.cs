using SkiaSharp;
using ZeppelinForms.Drawing.Imaging;

namespace ZeppelinForms.Skia;

public sealed class SkiaImageDecoder : ImageDecoder
{
    // Разумный потолок: десктоп-приложению незачем держать в памяти
    // разрешение выше того, что физически влезет на экран.
    public int MaxDimension { get; set; } = 2048;

    public static void Register() => Current = new SkiaImageDecoder();

    public override Image Decode(Stream stream)
    {
        using SKBitmap decoded = SKBitmap.Decode(stream)
            ?? throw new InvalidDataException("Не удалось декодировать изображение.");

        using SKBitmap scaled = Downscale(decoded, MaxDimension);

        using SKBitmap normalized = scaled.ColorType == SKColorType.Rgba8888
            ? scaled.Copy()
            : scaled.Copy(SKColorType.Rgba8888);

        return new Image(normalized.Width, normalized.Height, normalized.Bytes);
    }

    private static SKBitmap Downscale(SKBitmap source, int maxDimension)
    {
        if (source.Width <= maxDimension && source.Height <= maxDimension)
            return source.Copy(); // уже маленькое — просто независимая копия

        float scale = maxDimension / (float)Math.Max(source.Width, source.Height);
        int width = Math.Max(1, (int)(source.Width * scale));
        int height = Math.Max(1, (int)(source.Height * scale));

        return source.Resize(new SKSizeI(width, height), SKSamplingOptions.Default)
            ?? throw new InvalidDataException("Не удалось уменьшить изображение.");
    }
}
