using SkiaSharp;
using ZeppelinForms.Drawing.Imaging;

namespace ZeppelinForms.Skia;

public sealed class SkiaImageDecoder : ImageDecoder
{
    // Разумный потолок: десктоп-приложению незачем держать в памяти
    // разрешение выше того, что физически влезет на экран.
    public int MaxDimension { get; set; } = 2048;

    public static void Register() => Current = new SkiaImageDecoder();

    /// <summary>Зарегистрировать со своим потолком разрешения. Приложению,
    /// которое показывает картинки размером в иконку, держать их
    /// в полном разрешении незачем: 2048×2048 — это 16 МБ пикселей
    /// независимо от того, какого размера контрол.</summary>
    public static void Register(int maxDimension) =>
        Current = new SkiaImageDecoder { MaxDimension = maxDimension };

    /// <remarks>
    /// Каждая промежуточная копия — это полный буфер пикселей: для
    /// изображения 2048×2048 по 16 МБ за копию. Поэтому копируем ровно
    /// столько раз, сколько нужно: уменьшение — если не влезает в потолок,
    /// смена формата — если он не Rgba8888, и один перенос в управляемый
    /// массив, который забирает Image. Прежний код делал Copy безусловно
    /// и ещё раз Copy для формата, то есть держал в памяти четыре буфера
    /// вместо двух.
    /// </remarks>
    public override Image Decode(Stream stream)
    {
        // SKBitmap.Decode(Stream) на неудаче отдаёт null и молчит о причине.
        // Через SKCodec виден код ошибки, а сигнатура в сообщении сразу
        // отвечает на главный вопрос: это вообще картинка? Файл, скачанный
        // по пути, которого на сервере нет, часто оказывается страницей
        // ошибки, и начинается он с "<!DO"
        long length = stream.CanSeek ? stream.Length : -1;
        string signature = ReadSignature(stream);

        using SKCodec? codec = SKCodec.Create(stream, out SKCodecResult result);

        if (codec is null)
            throw new InvalidDataException(
                $"Не удалось декодировать изображение: {result}, " +
                $"{length} байт, сигнатура {signature}.");

        using SKBitmap decoded = SKBitmap.Decode(codec)
            ?? throw new InvalidDataException(
                $"Формат распознан как {codec.EncodedFormat}, " +
                $"но пиксели прочитать не удалось ({length} байт).");

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
                    ?? throw new InvalidDataException("Не удалось привести изображение к Rgba8888.");

                source = converted;
            }

            // Bytes сам делает управляемую копию — она и уезжает в Image,
            // а все нативные буферы освобождаются здесь же
            return new Image(source.Width, source.Height, source.Bytes);
        }
        finally
        {
            resized?.Dispose();
            converted?.Dispose();
        }
    }

    /// <summary>Первые байты в виде «41 42 43 44 (ABCD)» — по ним видно,
    /// подсунули ли вместо картинки что-то другое.</summary>
    private static string ReadSignature(Stream stream)
    {
        if (!stream.CanSeek) return "неизвестна";

        long position = stream.Position;

        try
        {
            Span<byte> head = stackalloc byte[4];
            int read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);

            if (read == 0) return "пусто";

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
            ?? throw new InvalidDataException("Не удалось уменьшить изображение.");
    }
}