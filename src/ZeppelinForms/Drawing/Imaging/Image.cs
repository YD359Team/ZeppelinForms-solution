namespace ZeppelinForms.Drawing.Imaging;

public sealed class Image
{
    /// <summary>One client for all network loads. A client per call exhausts
    /// sockets: a closed connection keeps its port for a while longer.</summary>
    private static readonly Lazy<HttpClient> Http = new(() => new HttpClient());

    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }

    public Image(int width, int height, byte[] pixels)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentException("Width and height must be positive.");

        if (pixels.Length < width * height * 4)
            throw new ArgumentException("The pixel buffer is smaller than width*height*4.", nameof(pixels));

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public static Image Load(Stream stream) => ImageDecoder.Current.Decode(stream);

    public static Image LoadFromFile(string path)
    {
        using var stream = File.OpenRead(path);

        try
        {
            return Load(stream);
        }
        catch (InvalidDataException exception)
        {
            // the decoder knows only the stream: without the path the message
            // doesn't say which of the application's resources is broken
            throw new InvalidDataException($"{exception.Message} File: {path}.", exception);
        }
    }

    public static Image LoadFromUri(Uri uri)
    {
        if (uri.IsFile)
            return LoadFromFile(uri.LocalPath);

        throw new NotSupportedException(
            "Use Image.LoadFromUriAsync for network URIs.");
    }

    public static async Task<Image> LoadFromUriAsync(Uri uri)
    {
        if (uri.IsFile)
            throw new NotSupportedException("Use Image.LoadFromUri for files.");

        using var res = await Http.Value.GetAsync(uri);
        res.EnsureSuccessStatusCode();
        return Load(await res.Content.ReadAsStreamAsync());
    }

    public static Image LoadAsset(string relativePath)
    {
        string fullPath = Path.Combine(Assets.Root, relativePath);
        return LoadFromFile(fullPath);
    }

    /// <summary>An absolute URI is loaded as a URI, anything else as a path
    /// to an application asset.</summary>
    /// <remarks>
    /// This used to call new Uri(value) first, and that constructor doesn't accept
    /// relative paths: <c>Image picture = "logo.png";</c> threw UriFormatException
    /// on every asset it was meant for.
    /// </remarks>
    public static implicit operator Image(string relativePath)
    {
        if (Uri.TryCreate(relativePath, UriKind.Absolute, out Uri? uri))
            return LoadFromUri(uri);

        return LoadAsset(relativePath);
    }
}

public enum ImageFlip
{
    None,
    Horizontal,   // a mirror over the vertical axis (formerly FlipX)
    Vertical,
    Both,
}

public enum ImageLayout
{
    Stretch,   // stretch over the whole control (the current behavior)
    None,      // draw at natural size from the top-left corner
    Center,
    Tile,
    Zoom,      // fit whole, keeping the proportions
}