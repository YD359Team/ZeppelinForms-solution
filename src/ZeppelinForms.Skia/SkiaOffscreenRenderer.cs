using SkiaSharp;
using System.Runtime.InteropServices;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Skia;

/// <summary>
/// Отрисовка в память без окна и графической сессии. Пригодна и для
/// снимковых тестов, и для экспорта содержимого в картинку.
/// </summary>
public sealed class SkiaOffscreenRenderer : IElementRenderer
{
    public static void Register() => ElementRenderer.Current = new SkiaOffscreenRenderer();

    public Image Render(UIElement element, int width, int height)
    {
        return RenderCore(width, height, canvas =>
        {
            var g = new SkiaGraphics(canvas);

            // Draw сдвигает канвас на Position — для снимка нужен
            // элемент в начале координат
            g.Save();
            g.Translate(-element.Position.X, -element.Position.Y);
            SkiaRenderer.DrawElement(element, g);
            g.Restore();
        });
    }

    /// <summary>Снимок всей формы вместе с оверлеями.</summary>
    public Image RenderForm(Form form, int width, int height, float scale = 1f) =>
        RenderCore(width, height, canvas => SkiaRenderer.Render(form, canvas, scale));

    private static Image RenderCore(int width, int height, Action<SKCanvas> draw)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);

        using SKSurface surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("Не удалось создать offscreen-поверхность.");

        surface.Canvas.Clear(SKColors.Transparent);
        draw(surface.Canvas);
        surface.Canvas.Flush();

        using SKImage snapshot = surface.Snapshot();
        using SKPixmap pixmap = snapshot.PeekPixels();

        byte[] pixels = new byte[width * height * 4];
        Marshal.Copy(pixmap.GetPixels(), pixels, 0, pixels.Length);

        return new Image(width, height, pixels);
    }

    /// <summary>Сохранить в PNG — чтобы результат можно было открыть глазами.</summary>
    public static void SavePng(Image image, string path)
    {
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Premul);

        using var bitmap = new SKBitmap(info);
        Marshal.Copy(image.Pixels, 0, bitmap.GetPixels(), image.Pixels.Length);

        using SKImage skImage = SKImage.FromBitmap(bitmap);
        using SKData data = skImage.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Create(path);

        data.SaveTo(stream);
    }
}
