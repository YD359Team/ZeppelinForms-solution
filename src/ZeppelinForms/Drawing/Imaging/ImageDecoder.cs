namespace ZeppelinForms.Drawing.Imaging;

public abstract class ImageDecoder
{
    public static ImageDecoder Current { get; set; } = new NotRegisteredImageDecoder();

    public abstract Image Decode(Stream stream);

    private sealed class NotRegisteredImageDecoder : ImageDecoder
    {
        public override Image Decode(Stream stream) =>
            throw new InvalidOperationException(
                "Декодер изображений не зарегистрирован. Вызовите " +
                "SkiaImageDecoder.Register() при старте приложения.");
    }
}