namespace ZeppelinForms.Drawing;

/// <summary>The order of a pixel's four bytes in memory. Premultiplied alpha in both.</summary>
public enum PixelFormat : byte
{
    /// <summary>Blue, green, red, alpha: a Windows DIB, an X11 ZPixmap of depth 24/32.</summary>
    Bgra8888,

    /// <summary>Red, green, blue, alpha: the browser's ImageData, an <see cref="Imaging.Image"/>.</summary>
    Rgba8888,
}

/// <summary>A buffer of pixels the platform owns and presents: a backend draws into it
/// and neither frees nor moves it.</summary>
/// <remarks>When the window is resized the platform makes a new buffer and asks for a
/// new surface over it: a surface over memory costs nothing to make.</remarks>
/// <param name="Pixels">The first byte of the top row.</param>
/// <param name="Width">In pixels.</param>
/// <param name="Height">In pixels.</param>
/// <param name="Stride">Bytes from the start of one row to the start of the next.</param>
/// <param name="Format">The order of the bytes of a pixel.</param>
public readonly record struct PixelBuffer(nint Pixels, int Width, int Height, int Stride, PixelFormat Format);