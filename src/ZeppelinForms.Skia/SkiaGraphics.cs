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
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Image, CachedImage> ImageCache = [];

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

            _canvas.DrawImage(skImage, target, SKSamplingOptions.Default);
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

    public override void DrawText(string text, Point position, Color color, Font font)
    {
        using var paint = new SKPaint { Color = new SKColor(color.R, color.G, color.B, color.A), IsAntialias = true };
        _canvas.DrawText(text, position.X, position.Y, SKTextAlign.Left, SkiaFontCache.Get(font), paint);
    }

    public override void DrawText(
        string text, Rectangle rect, Color color, Font font,
        HorizontalContentAlignment hAlign = HorizontalContentAlignment.Center,
        VerticalContentAlignment vAlign = VerticalContentAlignment.Center)
    {
        using var paint = new SKPaint { Color = new SKColor(color.R, color.G, color.B, color.A), IsAntialias = true };

        SKFont skFont = SkiaFontCache.Get(font);
        float textWidth = skFont.MeasureText(text, out SKRect bounds, paint);

        float x = hAlign switch
        {
            HorizontalContentAlignment.Left => rect.X,
            HorizontalContentAlignment.Right => rect.X + rect.Width - textWidth,
            _ => rect.X + (rect.Width - textWidth) / 2f,
        };

        float baselineY = vAlign switch
        {
            VerticalContentAlignment.Top => rect.Y - bounds.Top,
            VerticalContentAlignment.Bottom => rect.Y + rect.Height - bounds.Bottom,
            _ => rect.Y + rect.Height / 2f - bounds.MidY,
        };

        _canvas.DrawText(text, x, baselineY, SKTextAlign.Left, skFont, paint);
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

    public override void SaveLayer(float opacity)
    {
        using var paint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, (byte)Math.Clamp(opacity * 255f, 0, 255)),
        };

        _canvas.SaveLayer(paint);
    }

    public override void DrawShadow(Rectangle rect, BoxShadow shadow)
    {
        var color = new SKColor(shadow.Color.R, shadow.Color.G, shadow.Color.B, shadow.Color.A);

        using var paint = new SKPaint
        {
            Color = color,
            IsAntialias = true,
        };

        if (shadow.Blur > 0)
        {
            // sigma ≈ blur/2 — так радиус размытия совпадает с интуицией CSS
            paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, shadow.Blur / 2f);
        }

        var shadowRect = new SKRect(
            rect.X + shadow.OffsetX - shadow.Spread,
            rect.Y + shadow.OffsetY - shadow.Spread,
            rect.X + rect.Width + shadow.OffsetX + shadow.Spread,
            rect.Y + rect.Height + shadow.OffsetY + shadow.Spread);

        _canvas.DrawRect(shadowRect, paint);

        paint.MaskFilter?.Dispose();
    }

    public override void DrawLine(Point from, Point to, Color color, float width)
    {
        using var paint = new SKPaint
        {
            Color = new SKColor(color.R, color.G, color.B, color.A),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = width,
            StrokeCap = SKStrokeCap.Round,
        };

        _canvas.DrawLine(from.X, from.Y, to.X, to.Y, paint);
    }

    public override void DrawPolyline(ReadOnlySpan<Point> points, Color color, float width)
    {
        if (points.Length < 2) return;

        using var path = new SKPath();
        path.MoveTo(points[0].X, points[0].Y);

        for (int i = 1; i < points.Length; i++)
            path.LineTo(points[i].X, points[i].Y);

        using var paint = new SKPaint
        {
            Color = new SKColor(color.R, color.G, color.B, color.A),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = width,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,   // без этого угол галочки выглядит рубленым
        };

        _canvas.DrawPath(path, paint);
    }

    public override void DrawArc(Rectangle rect, float startAngle, float sweepAngle, Color color, float width)
    {
        using var paint = new SKPaint
        {
            Color = new SKColor(color.R, color.G, color.B, color.A),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = width,
            StrokeCap = SKStrokeCap.Round,
        };

        var oval = new SKRect(rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height);
        _canvas.DrawArc(oval, startAngle, sweepAngle, useCenter: false, paint);
    }

    public override void DrawSvgPath(string pathData, Rectangle rect, Color color, float strokeWidth = 0f)
    {
        using SKPath? path = SKPath.ParseSvgPathData(pathData);
        if (path is null) return;

        SKRect bounds = path.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        // вписываем path в rect с сохранением пропорций
        float scale = Math.Min(rect.Width / bounds.Width, rect.Height / bounds.Height);
        float dx = rect.X + (rect.Width - bounds.Width * scale) / 2f - bounds.Left * scale;
        float dy = rect.Y + (rect.Height - bounds.Height * scale) / 2f - bounds.Top * scale;

        using var paint = new SKPaint
        {
            Color = new SKColor(color.R, color.G, color.B, color.A),
            IsAntialias = true,
            Style = strokeWidth > 0 ? SKPaintStyle.Stroke : SKPaintStyle.Fill,
            StrokeWidth = strokeWidth,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };

        _canvas.Save();
        _canvas.Translate(dx, dy);
        _canvas.Scale(scale, scale);
        _canvas.DrawPath(path, paint);
        _canvas.Restore();
    }
}
