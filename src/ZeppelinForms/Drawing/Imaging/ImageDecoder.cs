namespace ZeppelinForms.Drawing.Imaging;

public abstract class ImageDecoder
{
    public static ImageDecoder Current { get; set; } = new NotRegisteredImageDecoder();

    public abstract Image Decode(Stream stream);

    private sealed class NotRegisteredImageDecoder : ImageDecoder
    {
        public override Image Decode(Stream stream) =>
            throw new InvalidOperationException(
                "No image decoder is registered. Call " +
                "SkiaImageDecoder.Register() at application startup.");
    }
}