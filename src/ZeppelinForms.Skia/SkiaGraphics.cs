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
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Image, CachedImage> ImageCache = new();

    public SkiaGraphics(SKCanvas canvas) => _canvas = canvas;

    private static SKImage GetOrCreate(Image image)
    {
        if (!ImageCache.TryGetValue(image, out CachedImage? cached))
        {
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(
                image.Pixels, System.Runtime.InteropServices.GCHandleType.Pinned);

            var info = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Premul);

            using var bitmap = new SKBitmap();
            bitmap.InstallPixels(info, handle.AddrOfPinnedObject(), info.RowBytes);

            SKImage skImage = SKImage.FromBitmap(bitmap);
            cached = new CachedImage(skImage, handle);
            ImageCache.Add(image, cached);
        }

        return cached.SkImage;
    }

    public override void DrawImage(
        Rectangle rect, Image image,
        ImageFlip flip = ImageFlip.None,
        ImageLayout layout = ImageLayout.Stretch)
    {
        SKImage skImage = GetOrCreate(image);

        _canvas.Save();
        _canvas.ClipRect(new SKRect(rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height));

        if (flip != ImageFlip.None)
        {
            float cx = rect.X + rect.Width / 2f;
            float cy = rect.Y + rect.Height / 2f;

            float sx = flip is ImageFlip.Horizontal or ImageFlip.Both ? -1f : 1f;
            float sy = flip is ImageFlip.Vertical or ImageFlip.Both ? -1f : 1f;

            // масштабируем вокруг центра области, иначе картинка уедет за пределы
            _canvas.Translate(cx, cy);
            _canvas.Scale(sx, sy);
            _canvas.Translate(-cx, -cy);
        }

        if (layout == ImageLayout.Tile)
        {
            using var shader = SKShader.CreateImage(
                skImage, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);

            using var paint = new SKPaint { Shader = shader };

            _canvas.Save();
            _canvas.Translate(rect.X, rect.Y);   // мозаика стартует от угла области
            _canvas.DrawRect(new SKRect(0, 0, rect.Width, rect.Height), paint);
            _canvas.Restore();
        }
        else
        {
            SKRect target = layout switch
            {
                ImageLayout.None => new SKRect(rect.X, rect.Y, rect.X + image.Width, rect.Y + image.Height),

                ImageLayout.Center => Centered(rect, image.Width, image.Height),

                ImageLayout.Zoom => Zoomed(rect, image.Width, image.Height),

                _ => new SKRect(rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height),
            };

            _canvas.DrawImage(skImage, target);
        }

        _canvas.Restore();
    }

    private static SKRect Centered(Rectangle rect, float w, float h)
    {
        float x = rect.X + (rect.Width - w) / 2f;
        float y = rect.Y + (rect.Height - h) / 2f;
        return new SKRect(x, y, x + w, y + h);
    }

    private static SKRect Zoomed(Rectangle rect, float w, float h)
    {
        float scale = Math.Min(rect.Width / w, rect.Height / h);
        return Centered(rect, w * scale, h * scale);
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

    public override void FillEllipse(Rectangle rect, Color color)
    {
        using var paint = new SKPaint { Color = new SKColor(color.R, color.G, color.B, color.A), IsAntialias = true };
        _canvas.DrawOval(new SKRect(rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height), paint);
    }

    public override void DrawEllipse(Rectangle rect, Color color, float width)
    {
        using var paint = new SKPaint
        {
            Color = new SKColor(color.R, color.G, color.B, color.A),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = width,
        };
        _canvas.DrawOval(new SKRect(rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height), paint);
    }

    public override void Save() => _canvas.Save();
    public override void ClipRect(Rectangle rect) => _canvas.ClipRect(new SKRect(rect.X, rect.Y, rect.Right, rect.Bottom));
    public override void Restore() => _canvas.Restore();
    public override void Translate(float dx, float dy) => _canvas.Translate(dx, dy);
    public override void Scale(float sx, float sy) => _canvas.Scale(sx, sy);
}
