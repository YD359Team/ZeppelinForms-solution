namespace ZeppelinForms.Drawing.Imaging;

public sealed class Image
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }

    public Image(int width, int height, byte[] pixels)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentException("Width/Height должны быть положительными.");

        if (pixels.Length < width * height * 4)
            throw new ArgumentException("Буфер пикселей меньше, чем width*height*4.", nameof(pixels));

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public static Image Load(Stream stream) => ImageDecoder.Current.Decode(stream);

    public static Image LoadFromFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Load(stream);
    }

    public static Image LoadFromUri(Uri uri)
    {
        if (uri.IsFile)
            return LoadFromFile(uri.LocalPath);

        throw new NotSupportedException(
            "Для сетевых URI используйте Image.LoadFromUriAsync (TODO).");
    }

    public static Image LoadAsset(string relativePath)
    {
        string fullPath = Path.Combine(AppContext.BaseDirectory, relativePath);
        return LoadFromFile(fullPath);
    }
}
