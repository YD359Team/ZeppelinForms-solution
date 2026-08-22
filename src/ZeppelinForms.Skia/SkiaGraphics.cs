using SkiaSharp;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Skia;

public sealed class SkiaGraphics : Graphics
{
    private readonly SKCanvas _canvas;
    private static readonly SKFont DefaultFont = new(SKTypeface.Default, 16);

    // Кэш "наш Image -> уже загруженный в Skia SKImage", чтобы не
    // перезаливать пиксели на каждый WM_PAINT. ConditionalWeakTable
    // сам подчистит запись, когда Image перестанет использоваться.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Image, SKImage> ImageCache = new();

    public SkiaGraphics(SKCanvas canvas) => _canvas = canvas;

    public override void DrawImage(Rectangle rect, Image image)
    {
        if (!ImageCache.TryGetValue(image, out SKImage? skImage))
        {
            using var bitmap = new SKBitmap(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            System.Runtime.InteropServices.Marshal.Copy(image.Pixels, 0, bitmap.GetPixels(), image.Pixels.Length);

            skImage = SKImage.FromBitmap(bitmap);
            ImageCache.Add(image, skImage);
        }

        _canvas.DrawImage(skImage, new SKRect(rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height));
    }

    public override void FillRectangle(Rectangle rect, Color color)
    {
        using var paint = new SKPaint { Color = new SKColor(color.R, color.G, color.B, color.A), IsAntialias = true };
        _canvas.DrawRect(new SKRect(rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height), paint);
    }

    public override void DrawRectangle(Rectangle rect, Color color, float width)
    {
        using var paint = new SKPaint
        {
            Color = new SKColor(color.R, color.G, color.B, color.A),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = width,
            IsStroke = true,
        };
        _canvas.DrawRect(new SKRect(rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height), paint);
    }

    public override void DrawText(string text, Point position, Color color)
    {
        using var paint = new SKPaint { Color = new SKColor(color.R, color.G, color.B, color.A), IsAntialias = true };
        _canvas.DrawText(text, position.X, position.Y, SKTextAlign.Left, DefaultFont, paint);
    }

    public override void DrawText(
        string text, Rectangle rect, Color color,
        HorizontalAlign hAlign = HorizontalAlign.Center,
        VerticalAlign vAlign = VerticalAlign.Center)
    {
        using var paint = new SKPaint { Color = new SKColor(color.R, color.G, color.B, color.A), IsAntialias = true };

        float textWidth = DefaultFont.MeasureText(text, out SKRect bounds, paint);

        float x = hAlign switch
        {
            HorizontalAlign.Left => rect.X,
            HorizontalAlign.Right => rect.X + rect.Width - textWidth,
            _ => rect.X + (rect.Width - textWidth) / 2f,
        };

        float baselineY = vAlign switch
        {
            VerticalAlign.Top => rect.Y - bounds.Top,
            VerticalAlign.Bottom => rect.Y + rect.Height - bounds.Bottom,
            _ => rect.Y + rect.Height / 2f - bounds.MidY,
        };

        _canvas.DrawText(text, x, baselineY, SKTextAlign.Left, DefaultFont, paint);
    }

    public override void Save() => _canvas.Save();
    public override void Restore() => _canvas.Restore();
    public override void Translate(float dx, float dy) => _canvas.Translate(dx, dy);
}