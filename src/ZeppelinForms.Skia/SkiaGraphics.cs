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

    private static SKRoundRect MakeRoundRect(Rectangle rect, CornerRadius radius)
    {
        var skRect = new SKRect(rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height);
        var rounded = new SKRoundRect();

        // порядок углов в Skia: TL, TR, BR, BL — по часовой от левого верхнего
        rounded.SetRectRadii(skRect,
        [
            new SKPoint(radius.TopLeft, radius.TopLeft),
            new SKPoint(radius.TopRight, radius.TopRight),
            new SKPoint(radius.BottomRight, radius.BottomRight),
            new SKPoint(radius.BottomLeft, radius.BottomLeft),
        ]);

        return rounded;
    }

    public override void FillRoundRectangle(Rectangle rect, CornerRadius radius, Color color)
    {
        if (radius.IsZero) { FillRectangle(rect, color); return; }

        using var paint = new SKPaint { Color = new SKColor(color.R, color.G, color.B, color.A), IsAntialias = true };
        using var rounded = MakeRoundRect(rect, radius);
        _canvas.DrawRoundRect(rounded, paint);
    }

    public override void DrawRoundRectangle(Rectangle rect, CornerRadius radius, Color color, float width)
    {
        if (radius.IsZero) { DrawRectangle(rect, color, width); return; }

        using var paint = new SKPaint
        {
            Color = new SKColor(color.R, color.G, color.B, color.A),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = width,
        };

        using var rounded = MakeRoundRect(rect, radius);
        _canvas.DrawRoundRect(rounded, paint);
    }

    public override void ClipRoundRect(Rectangle rect, CornerRadius radius)
    {
        if (radius.IsZero) { ClipRect(rect); return; }

        using var rounded = MakeRoundRect(rect, radius);
        _canvas.ClipRoundRect(rounded, antialias: true);
    }

    public override void Rotate(float degrees) => _canvas.RotateDegrees(degrees);

    public override void DrawText(string text, Point position, Color color, Font font)
    {
        using var paint = new SKPaint { Color = new SKColor(color.R, color.G, color.B, color.A), IsAntialias = true };

        float x = position.X;

        foreach ((string run, SKFont runFont) in SkiaFontCache.SplitRuns(text, font))
        {
            _canvas.DrawText(run, x, position.Y, SKTextAlign.Left, runFont, paint);
            x += runFont.MeasureText(run);
        }
    }

    public override void DrawText(
        string text, Rectangle rect, Color color, Font font,
        HorizontalContentAlignment hAlign = HorizontalContentAlignment.Center,
        VerticalContentAlignment vAlign = VerticalContentAlignment.Center)
    {
        using var paint = new SKPaint { Color = new SKColor(color.R, color.G, color.B, color.A), IsAntialias = true };

        SKFont baseFont = SkiaFontCache.Get(font);

        // ширина считается по тем же участкам, что и рисование, иначе
        // выравнивание разъедется на строках с эмодзи
        float textWidth = 0;
        foreach ((string run, SKFont runFont) in SkiaFontCache.SplitRuns(text, font))
            textWidth += runFont.MeasureText(run);

        baseFont.MeasureText(text, out SKRect bounds, paint);

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

        foreach ((string run, SKFont runFont) in SkiaFontCache.SplitRuns(text, font))
        {
            _canvas.DrawText(run, x, baselineY, SKTextAlign.Left, runFont, paint);
            x += runFont.MeasureText(run);
        }
    }

    public override void FillPie(Rectangle rect, float startAngle, float sweepAngle, Color color)
    {
        using var paint = new SKPaint
        {
            Color = new SKColor(color.R, color.G, color.B, color.A),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };

        var oval = new SKRect(rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height);
        _canvas.DrawArc(oval, startAngle, sweepAngle, useCenter: true, paint);
    }

    public override void DrawRuns(
      IReadOnlyList<TextRun> runs, Rectangle rect, Font baseFont, Color baseColor,
      HorizontalContentAlignment hAlign = HorizontalContentAlignment.Center,
      VerticalContentAlignment vAlign = VerticalContentAlignment.Center)
    {
        if (runs.Count == 0) return;

        float totalWidth = 0;
        float maxAscent = 0, maxDescent = 0;

        // первый проход — габариты, чтобы знать, откуда начинать
        foreach (TextRun run in runs)
        {
            Font font = run.Font ?? baseFont;
            SKFont skFont = SkiaFontCache.Get(font);

            foreach ((string piece, SKFont pieceFont) in SkiaFontCache.SplitRuns(run.Text, font))
                totalWidth += pieceFont.MeasureText(piece);

            SKFontMetrics metrics = skFont.Metrics;
            maxAscent = Math.Max(maxAscent, -metrics.Ascent);
            maxDescent = Math.Max(maxDescent, metrics.Descent);
        }

        float lineHeight = maxAscent + maxDescent;

        float x = hAlign switch
        {
            HorizontalContentAlignment.Left => rect.X,
            HorizontalContentAlignment.Right => rect.X + rect.Width - totalWidth,
            _ => rect.X + (rect.Width - totalWidth) / 2f,
        };

        float top = vAlign switch
        {
            VerticalContentAlignment.Top => rect.Y,
            VerticalContentAlignment.Bottom => rect.Y + rect.Height - lineHeight,
            _ => rect.Y + (rect.Height - lineHeight) / 2f,
        };

        // общая базовая линия: прогоны разного размера должны стоять на одной линии,
        // а не каждый по центру своего прямоугольника
        float baseline = top + maxAscent;

        foreach (TextRun run in runs)
        {
            Font font = run.Font ?? baseFont;
            Color color = run.Color ?? baseColor;

            float runStart = x;
            float runWidth = 0;

            foreach ((string piece, SKFont pieceFont) in SkiaFontCache.SplitRuns(run.Text, font))
                runWidth += pieceFont.MeasureText(piece);

            if (run.Background is Color background)
            {
                using var backgroundPaint = new SKPaint
                {
                    Color = new SKColor(background.R, background.G, background.B, background.A),
                };

                _canvas.DrawRect(
                    new SKRect(runStart, top, runStart + runWidth, top + lineHeight),
                    backgroundPaint);
            }

            using var paint = new SKPaint
            {
                Color = new SKColor(color.R, color.G, color.B, color.A),
                IsAntialias = true,
            };

            foreach ((string piece, SKFont pieceFont) in SkiaFontCache.SplitRuns(run.Text, font))
            {
                _canvas.DrawText(piece, x, baseline, SKTextAlign.Left, pieceFont, paint);
                x += pieceFont.MeasureText(piece);
            }

            if (run.Underline || run.Strikethrough)
            {
                using var linePaint = new SKPaint
                {
                    Color = paint.Color,
                    StrokeWidth = Math.Max(1f, font.Size / 14f),
                    IsAntialias = true,
                };

                if (run.Underline)
                {
                    float y = baseline + font.Size * 0.12f;
                    _canvas.DrawLine(runStart, y, runStart + runWidth, y, linePaint);
                }

                if (run.Strikethrough)
                {
                    float y = baseline - font.Size * 0.28f;
                    _canvas.DrawLine(runStart, y, runStart + runWidth, y, linePaint);
                }
            }
        }
    }

    public override void SaveDisabledLayer(float opacity, float desaturation)
    {
        // матрица цвета: смешиваем каждый канал в сторону яркости,
        // получая частичное обесцвечивание без ручного пересчёта пикселей
        float s = 1f - Math.Clamp(desaturation, 0f, 1f);

        float rr = 0.213f + 0.787f * s, rg = 0.715f - 0.715f * s, rb = 0.072f - 0.072f * s;
        float gr = 0.213f - 0.213f * s, gg = 0.715f + 0.285f * s, gb = 0.072f - 0.072f * s;
        float br = 0.213f - 0.213f * s, bg = 0.715f - 0.715f * s, bb = 0.072f + 0.928f * s;

        float[] matrix =
        [
            rr, rg, rb, 0, 0,
            gr, gg, gb, 0, 0,
            br, bg, bb, 0, 0,
            0,  0,  0,  Math.Clamp(opacity, 0f, 1f), 0,
        ];

        using var filter = SKColorFilter.CreateColorMatrix(matrix);
        using var paint = new SKPaint { ColorFilter = filter };

        _canvas.SaveLayer(paint);
    }

    public override void ClipCircle(Point center, float radius)
    {
        using var path = new SKPath();
        path.AddCircle(center.X, center.Y, Math.Max(0, radius));

        _canvas.ClipPath(path, antialias: true);
    }

    public override void SaveBlurLayer(float radius)
    {
        using var filter = SKImageFilter.CreateBlur(radius / 2f, radius / 2f);
        using var paint = new SKPaint { ImageFilter = filter };

        _canvas.SaveLayer(paint);
    }

    public override void BlurBackdrop(Rectangle bounds, float radius)
    {
        if (radius <= 0 || bounds.Width <= 0 || bounds.Height <= 0) return;

        // поверхность принадлежит вызывающей стороне: захватывать её
        // в using нельзя, иначе следующий кадр упадёт
        SKSurface? surface = _canvas.Surface;
        if (surface is null) return;

        var rect = new SKRect(bounds.X, bounds.Y, bounds.X + bounds.Width, bounds.Y + bounds.Height);

        // область в пикселях устройства: канвас может быть отмасштабирован под DPI
        SKMatrix matrix = _canvas.TotalMatrix;
        SKRect deviceRect = matrix.MapRect(rect);

        var subset = SKRectI.Round(deviceRect);
        subset.Intersect(new SKRectI(0, 0, surface.Canvas.DeviceClipBounds.Right, surface.Canvas.DeviceClipBounds.Bottom));

        if (subset.IsEmpty) return;

        // Snapshot(subset) на GPU остаётся текстурой и не тянет данные в CPU —
        // именно поэтому берём подобласть, а не весь кадр
        using SKImage? snapshot = surface.Snapshot(subset);
        if (snapshot is null) return;

        using var filter = SKImageFilter.CreateBlur(radius / 2f, radius / 2f, SKShaderTileMode.Clamp);
        using var paint = new SKPaint { ImageFilter = filter };

        _canvas.Save();
        _canvas.ClipRect(rect);

        // рисуем снимок обратно на его же место, но уже размытым
        _canvas.DrawImage(snapshot, rect, SKSamplingOptions.Default, paint);

        _canvas.Restore();
    }

    public override void Skew(float sx, float sy)
    {
        // SKMatrix.CreateSkew задаёт наклон относительно начала координат;
        // точку поворота выставляет вызывающий через Translate
        _canvas.Concat(SKMatrix.CreateSkew(sx, sy));
    }

    public override void FillNoise(Rectangle bounds, float opacity)
    {
        if (opacity <= 0 || bounds.Width <= 0 || bounds.Height <= 0) return;

        using SKShader shader = NoiseShader;

        byte alpha = (byte)Math.Clamp(opacity * 255f, 0, 255);

        using var paint = new SKPaint
        {
            Shader = shader,
            Color = new SKColor(255, 255, 255, alpha),
        };

        _canvas.DrawRect(
            new SKRect(bounds.X, bounds.Y, bounds.X + bounds.Width, bounds.Y + bounds.Height),
            paint);
    }

    // шум генерируется один раз: процедурная текстура одинакова для всех
    // элементов, а пересоздавать её на каждый кадр слишком дорого
    private static SKShader NoiseShader =>
        SKShader.CreatePerlinNoiseFractalNoise(0.8f, 0.8f, 2, 0f);

    public override void DrawReflection(Rectangle bounds, float heightRatio, float gap, float startOpacity)
    {
        if (heightRatio <= 0 || bounds.Width <= 0 || bounds.Height <= 0) return;

        SKSurface? surface = _canvas.Surface;
        if (surface is null) return;

        var source = new SKRect(bounds.X, bounds.Y, bounds.X + bounds.Width, bounds.Y + bounds.Height);

        SKRect deviceRect = _canvas.TotalMatrix.MapRect(source);
        var subset = SKRectI.Round(deviceRect);

        if (subset.IsEmpty) return;

        using SKImage? snapshot = surface.Snapshot(subset);
        if (snapshot is null) return;

        float reflectionHeight = bounds.Height * Math.Clamp(heightRatio, 0f, 1f);
        float top = bounds.Y + bounds.Height + gap;

        var target = new SKRect(bounds.X, top, bounds.X + bounds.Width, top + reflectionHeight);

        // градиент от полупрозрачного к нулю: отражение должно растворяться,
        // а не обрываться по краю
        using SKShader fade = SKShader.CreateLinearGradient(
            new SKPoint(target.Left, target.Top),
            new SKPoint(target.Left, target.Bottom),
            [
                new SKColor(255, 255, 255, (byte)Math.Clamp(startOpacity * 255f, 0, 255)),
                new SKColor(255, 255, 255, 0),
            ],
            null,
            SKShaderTileMode.Clamp);

        using var paint = new SKPaint { Shader = fade, BlendMode = SKBlendMode.DstIn };

        _canvas.Save();
        _canvas.ClipRect(target);

        // отражаем по вертикали относительно верхней границы отражения
        _canvas.Translate(0, target.Top);
        _canvas.Scale(1, -1);
        _canvas.Translate(0, -target.Top - reflectionHeight);

        var flipped = new SKRect(
            bounds.X, target.Top,
            bounds.X + bounds.Width, target.Top + reflectionHeight);

        // слой нужен, чтобы затухание применилось к отражению,
        // а не к тому, что уже нарисовано под ним
        _canvas.SaveLayer(null);
        _canvas.DrawImage(snapshot, flipped, SKSamplingOptions.Default);
        _canvas.DrawRect(flipped, paint);
        _canvas.Restore();

        _canvas.Restore();
    }

    public override void FillGradient(Rectangle bounds, CornerRadius radius, GradientStop[] stops, float angle)
    {
        if (stops.Length == 0 || bounds.Width <= 0 || bounds.Height <= 0) return;

        var rect = new SKRect(bounds.X, bounds.Y, bounds.X + bounds.Width, bounds.Y + bounds.Height);

        // направление задаём углом: 0 — слева направо, 90 — сверху вниз
        float radians = angle * MathF.PI / 180f;

        float halfWidth = bounds.Width / 2f;
        float halfHeight = bounds.Height / 2f;

        float dx = MathF.Cos(radians) * halfWidth;
        float dy = MathF.Sin(radians) * halfHeight;

        float cx = bounds.X + halfWidth;
        float cy = bounds.Y + halfHeight;

        SKColor[] colors = new SKColor[stops.Length];
        float[] positions = new float[stops.Length];

        for (int i = 0; i < stops.Length; i++)
        {
            Color c = stops[i].Color;
            colors[i] = new SKColor(c.R, c.G, c.B, c.A);
            positions[i] = Math.Clamp(stops[i].Offset, 0f, 1f);
        }

        using SKShader shader = SKShader.CreateLinearGradient(
            new SKPoint(cx - dx, cy - dy),
            new SKPoint(cx + dx, cy + dy),
            colors, positions, SKShaderTileMode.Clamp);

        using var paint = new SKPaint { Shader = shader, IsAntialias = true };

        if (radius.IsZero)
        {
            _canvas.DrawRect(rect, paint);
            return;
        }

        using SKRoundRect rounded = MakeRoundRect(bounds, radius);
        _canvas.DrawRoundRect(rounded, paint);
    }
}
