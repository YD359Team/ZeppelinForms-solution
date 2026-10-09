using SkiaSharp;
using ZeppelinForms.Diagnostics;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Skia;

public sealed class SkiaGraphics : Graphics
{
    // A cache "our Image -> the SKImage already uploaded into Skia", so that the
    // pixels aren't re-uploaded on every WM_PAINT. ConditionalWeakTable cleans up
    // the entry by itself when the Image is no longer in use.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Image, SKImage> ImageCache = [];

    // ===== Diagnostics =====

    /// <summary>How many brushes were created on this thread. Zero or two —
    /// the pool works; more would mean the brushes are being recreated.</summary>
    [ThreadStatic]
    private static int _paintsCreated;

    internal static int PaintsCreated => _paintsCreated;

    /// <summary>Images in the cache. ConditionalWeakTable gives no Count, so we count
    /// ourselves: entries are added only in GetOrCreate, and the collector removes
    /// them — the value shows how many uploads were made, not how many are alive now.</summary>
    private static int _imagesUploaded;

    internal static int ImagesUploaded => Volatile.Read(ref _imagesUploaded);

    #region Brush pool

    // Brushes are reused instead of being created for every primitive: SKPaint is
    // a wrapper over a native object, and allocating it followed by a Dispose for
    // every fill produced hundreds of kilobytes of garbage per frame.
    //
    // ThreadStatic rather than a shared static: drawing goes from the UI thread,
    // but xunit snapshot tests run in parallel, and a shared brush would become
    // a race. Brushes with shaders and filters don't go into the pool — the
    // reference to an already destroyed shader would have to be reset in them,
    // and they are called rarely, for effects and gradients.

    [ThreadStatic]
    private static SKPaint? _fillPaint;

    [ThreadStatic]
    private static SKPaint? _strokePaint;

    private const string PoolContract =
        "The brush pool has created more than two instances. A brush is being lost " +
        "or reset between frames somewhere — the point of the pool is that there " +
        "are exactly two of them per thread.";

    private static SKPaint FillPaint(Color color)
    {
        SKPaint? paint = _fillPaint;

        // this block used to contain a second, unreachable "paint is null" check
        // nested inside the first one, and the pool contract below sat in it —
        // for the fill brush the check never ran at all
        if (paint is null)
        {
            paint = _fillPaint = new SKPaint();
            _paintsCreated++;

            ZfContract.Require(_paintsCreated <= 2, PoolContract);
        }

        paint.Reset();
        paint.Color = new SKColor(color.R, color.G, color.B, color.A);
        paint.IsAntialias = true;
        paint.Style = SKPaintStyle.Fill;

        return paint;
    }

    private static SKPaint StrokePaint(
        Color color,
        float width,
        SKStrokeCap cap = SKStrokeCap.Butt,
        SKStrokeJoin join = SKStrokeJoin.Miter)
    {
        SKPaint? paint = _strokePaint;

        if (paint is null)
        {
            paint = _strokePaint = new SKPaint();
            _paintsCreated++;

            ZfContract.Require(_paintsCreated <= 2, PoolContract);
        }

        paint.Reset();
        paint.Color = new SKColor(color.R, color.G, color.B, color.A);
        paint.IsAntialias = true;
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = width;
        paint.StrokeCap = cap;
        paint.StrokeJoin = join;

        return paint;
    }

    #endregion

    // The canvas is swapped for the duration of a capture: the element is drawn
    // into an offscreen layer, not to the screen. A stack — because effects nest.
    private SKCanvas _canvas;
    private readonly Stack<CaptureFrame> _captures = new();

    public SkiaGraphics(SKCanvas canvas) => _canvas = canvas;

    private static SKImage GetOrCreate(Image image)
    {
        if (!ImageCache.TryGetValue(image, out SKImage? cached))
        {
            var info = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Premul);

            // FromPixelCopy rather than InstallPixels: pinning the buffer through
            // a GCHandle would be a strong GC root living apart from the table.
            // ConditionalWeakTable doesn't release its values, so such a handle was
            // never freed and the pixel array stayed pinned until the process ended.
            // The copy costs one upload per image and is released by the SKImage
            // finalizer together with the table entry.
            cached = SKImage.FromPixelCopy(info, image.Pixels, info.RowBytes)
                ?? throw new InvalidOperationException(
                    "Could not upload the image into Skia.");

            ImageCache.Add(image, cached);
            Interlocked.Increment(ref _imagesUploaded);
        }

        return cached;
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

            // scale around the area's center, otherwise the picture would move outside
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
            _canvas.Translate(rect.X, rect.Y);   // the tiling starts from the area's corner
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
        SKPaint paint = FillPaint(color);
        _canvas.DrawRect(new SKRect(rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height), paint);
    }

    public override void DrawRectangle(Rectangle rect, Color color, float width)
    {
        SKPaint paint = StrokePaint(color, width);
        _canvas.DrawRect(new SKRect(rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height), paint);
    }

    public override void FillEllipse(Rectangle rect, Color color)
    {
        SKPaint paint = FillPaint(color);
        _canvas.DrawOval(new SKRect(rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height), paint);
    }

    public override void DrawEllipse(Rectangle rect, Color color, float width)
    {
        SKPaint paint = StrokePaint(color, width);
        _canvas.DrawOval(new SKRect(rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height), paint);
    }

    public override void Save() => _canvas.Save();
    public override void ClipRect(Rectangle rect) => _canvas.ClipRect(new SKRect(rect.X, rect.Y, rect.Right, rect.Bottom));
    public override void Restore() => _canvas.Restore();
    public override void Translate(float dx, float dy) => _canvas.Translate(dx, dy);
    public override void Scale(float sx, float sy) => _canvas.Scale(sx, sy);

    public override void SaveLayer(float opacity)
    {
        // SaveLayer copies the brush for itself, so the one reused
        // from the pool is safe here
        SKPaint paint = FillPaint(new Color(
            (byte)Math.Clamp(opacity * 255f, 0, 255), 255, 255, 255));

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
            // sigma ≈ blur/2 — this way the blur radius matches the CSS intuition
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
        SKPaint paint = StrokePaint(color, width, SKStrokeCap.Round);
        _canvas.DrawLine(from.X, from.Y, to.X, to.Y, paint);
    }

    public override void DrawPolyline(ReadOnlySpan<Point> points, Color color, float width)
    {
        if (points.Length < 2) return;

        // SKPath.MoveTo/LineTo are declared obsolete: path building
        // moved to SKPathBuilder, and SKPath itself became immutable
        using var builder = new SKPathBuilder();
        builder.MoveTo(points[0].X, points[0].Y);

        for (int i = 1; i < points.Length; i++)
            builder.LineTo(points[i].X, points[i].Y);

        using SKPath path = builder.Detach();

        // StrokeJoin.Round — without it the corner of a check mark looks chopped
        SKPaint paint = StrokePaint(color, width, SKStrokeCap.Round, SKStrokeJoin.Round);

        _canvas.DrawPath(path, paint);
    }

    public override void DrawArc(Rectangle rect, float startAngle, float sweepAngle, Color color, float width)
    {
        SKPaint paint = StrokePaint(color, width, SKStrokeCap.Round);

        var oval = new SKRect(rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height);
        _canvas.DrawArc(oval, startAngle, sweepAngle, useCenter: false, paint);
    }

    public override void DrawSvgPath(string pathData, Rectangle rect, Color color, float strokeWidth = 0f)
    {
        using SKPath? path = SKPath.ParseSvgPathData(pathData);
        if (path is null) return;

        SKRect bounds = path.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        // fit the path into rect keeping the proportions
        float scale = Math.Min(rect.Width / bounds.Width, rect.Height / bounds.Height);
        float dx = rect.X + (rect.Width - bounds.Width * scale) / 2f - bounds.Left * scale;
        float dy = rect.Y + (rect.Height - bounds.Height * scale) / 2f - bounds.Top * scale;

        SKPaint paint = strokeWidth > 0
            ? StrokePaint(color, strokeWidth, SKStrokeCap.Round, SKStrokeJoin.Round)
            : FillPaint(color);

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

        // the corner order in Skia: TL, TR, BR, BL — clockwise from the top left
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

        SKPaint paint = FillPaint(color);
        using var rounded = MakeRoundRect(rect, radius);
        _canvas.DrawRoundRect(rounded, paint);
    }

    public override void DrawRoundRectangle(Rectangle rect, CornerRadius radius, Color color, float width)
    {
        if (radius.IsZero) { DrawRectangle(rect, color, width); return; }

        SKPaint paint = StrokePaint(color, width);
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
        if (string.IsNullOrEmpty(text)) return;

        CachedLine line = SkiaFontCache.GetLine(text, font);

        // the outline goes under the glyphs: the fill then covers its inner half
        DrawOutline(line, position.X, position.Y);

        SKPaint paint = FillPaint(color);

        float x = position.X;

        for (int i = 0; i < line.Runs.Length; i++)
        {
            // the blob was already built on the first draw of this line;
            // DrawText(string, ...) would rebuild it every frame
            if (line.GetBlob(i) is { } blob)
                _canvas.DrawText(blob, x, position.Y, paint);

            x += line.Runs[i].Width;
        }

        DrawDecorations(TextEffects.Decorations, position.X, line.Width, position.Y, font, color);
    }

    public override void DrawText(
        string text, Rectangle rect, Color color, Font font,
        HorizontalContentAlignment hAlign = HorizontalContentAlignment.Center,
        VerticalContentAlignment vAlign = VerticalContentAlignment.Center)
    {
        if (string.IsNullOrEmpty(text)) return;

        // the width is computed from the same pieces as the drawing, otherwise
        // the alignment drifts on lines with emoji
        CachedLine line = SkiaFontCache.GetLine(text, font);

        float textWidth = line.Width;
        SKRect bounds = line.Bounds;

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

        // the rectangle sets not only the alignment but also the bounds:
        // without a clip, a caption that doesn't fit its box is drawn over its
        // neighbours. The clip is set only on overflow — an unconditional one
        // would cost a Save/Restore for every line in the frame
        bool clipped = textWidth > rect.Width;

        if (clipped)
        {
            Save();
            ClipRect(rect);
        }

        float start = x;

        // the outline goes under the glyphs: the fill then covers its inner half
        DrawOutline(line, start, baselineY);

        SKPaint paint = FillPaint(color);

        for (int i = 0; i < line.Runs.Length; i++)
        {
            if (line.GetBlob(i) is { } blob)
                _canvas.DrawText(blob, x, baselineY, paint);

            x += line.Runs[i].Width;
        }

        DrawDecorations(TextEffects.Decorations, start, textWidth, baselineY, font, color);

        if (clipped)
            Restore();
    }

    /// <summary>The current <see cref="Graphics.TextEffects"/> outline of a line: the same
    /// blobs stroked twice as wide as the outline reaches out, before the fill.</summary>
    private void DrawOutline(CachedLine line, float x, float baseline)
    {
        TextEffects effects = TextEffects;

        if (!effects.HasOutline) return;

        // round joins: miters spike out of the sharp corners of letters like A and V
        SKPaint paint = StrokePaint(effects.OutlineColor, effects.OutlineWidth * 2f, join: SKStrokeJoin.Round);

        for (int i = 0; i < line.Runs.Length; i++)
        {
            if (line.GetBlob(i) is { } blob)
                _canvas.DrawText(blob, x, baseline, paint);

            x += line.Runs[i].Width;
        }
    }

    /// <summary>Decoration lines along a piece of text, at the same heights as the
    /// underline and strikethrough of <see cref="TextRun"/>.</summary>
    private void DrawDecorations(TextDecorations decorations, float x, float width, float baseline, Font font, Color textColor)
    {
        if (decorations == TextDecorations.None || width <= 0f) return;

        SKPaint paint = StrokePaint(TextEffects.LineColor(textColor), Math.Max(1f, font.Size / 14f));

        if ((decorations & TextDecorations.Underline) != 0)
        {
            float y = baseline + font.Size * 0.12f;
            _canvas.DrawLine(x, y, x + width, y, paint);
        }

        if ((decorations & TextDecorations.Strikethrough) != 0)
        {
            float y = baseline - font.Size * 0.28f;
            _canvas.DrawLine(x, y, x + width, y, paint);
        }

        if ((decorations & TextDecorations.Overline) != 0)
        {
            // the ascent is negative: up from the baseline to the top of the capitals
            float y = baseline + SkiaFontCache.Get(font).Metrics.Ascent * 0.92f;
            _canvas.DrawLine(x, y, x + width, y, paint);
        }
    }

    public override void FillPie(Rectangle rect, float startAngle, float sweepAngle, Color color)
    {
        SKPaint paint = FillPaint(color);

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

        // the first pass — the extents, to know where to start from
        foreach (TextRun run in runs)
        {
            Font font = run.Font ?? baseFont;

            totalWidth += SkiaFontCache.GetLine(run.Text, font).Width;

            SKFontMetrics metrics = SkiaFontCache.Get(font).Metrics;
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

        // a shared baseline: runs of different sizes must stand on one line,
        // not each in the middle of its own rectangle
        float baseline = top + maxAscent;

        foreach (TextRun run in runs)
        {
            Font font = run.Font ?? baseFont;
            Color color = run.Color ?? baseColor;

            CachedLine line = SkiaFontCache.GetLine(run.Text, font);

            float runStart = x;
            float runWidth = line.Width;

            if (run.Background is Color background)
            {
                SKPaint backgroundPaint = FillPaint(background);

                _canvas.DrawRect(
                    new SKRect(runStart, top, runStart + runWidth, top + lineHeight),
                    backgroundPaint);
            }

            DrawOutline(line, x, baseline);

            // the background is already drawn, the brush can be reconfigured for
            // the text: the calls go one after another, there is no overlap
            SKPaint paint = FillPaint(color);

            for (int i = 0; i < line.Runs.Length; i++)
            {
                if (line.GetBlob(i) is { } blob)
                    _canvas.DrawText(blob, x, baseline, paint);

                x += line.Runs[i].Width;
            }

            // the run's own lines add to the element's: a link in an underlined
            // paragraph is underlined once, a struck run in it — both
            TextDecorations decorations = TextEffects.Decorations;

            if (run.Underline) decorations |= TextDecorations.Underline;
            if (run.Strikethrough) decorations |= TextDecorations.Strikethrough;

            DrawDecorations(decorations, runStart, runWidth, baseline, font, color);
        }
    }

    public override void SaveDisabledLayer(float opacity, float desaturation)
    {
        // a color matrix: each channel is mixed toward the luminance,
        // giving partial desaturation without recomputing pixels by hand
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
        using var builder = new SKPathBuilder();
        builder.AddCircle(center.X, center.Y, Math.Max(0, radius));

        using SKPath path = builder.Detach();

        _canvas.ClipPath(path, antialias: true);
    }

    public override void SaveBlurLayer(float radius)
    {
        using var filter = SKImageFilter.CreateBlur(radius / 2f, radius / 2f);
        using var paint = new SKPaint { ImageFilter = filter };

        _canvas.SaveLayer(paint);
    }

    /// <summary>A snapshot of what is already drawn under a local rectangle, and the
    /// part of that rectangle it actually covers, in the same local coordinates.</summary>
    /// <remarks>
    /// The area is cut to the visible part of the canvas: beyond the surface's edge
    /// or the current clip there is nothing, or something stale. The part actually
    /// taken is mapped back through the inverse matrix, and the caller draws the
    /// snapshot exactly there. Previously the cut snapshot was drawn back into the
    /// whole rectangle and came out stretched for an element partially outside
    /// the window or a scroll clip.
    /// </remarks>
    private SKImage? SnapshotUnder(SKRect local, out SKRect taken)
    {
        taken = SKRect.Empty;

        // the surface belongs to the caller: it must not be taken into a using,
        // otherwise the next frame would crash
        SKSurface? surface = _canvas.Surface;
        if (surface is null) return null;

        // the area in device pixels: the canvas may be scaled for DPI
        SKMatrix matrix = _canvas.TotalMatrix;
        if (!matrix.TryInvert(out SKMatrix inverse)) return null;

        var subset = SKRectI.Round(matrix.MapRect(local));
        subset.Intersect(_canvas.DeviceClipBounds);

        if (subset.IsEmpty) return null;

        // Snapshot(subset) on the GPU stays a texture and doesn't pull data into
        // the CPU — that is exactly why a sub-area is taken rather than the whole frame
        SKImage? snapshot = surface.Snapshot(subset);
        if (snapshot is null) return null;

        taken = inverse.MapRect(subset);
        return snapshot;
    }

    public override void BlurBackdrop(Rectangle bounds, float radius)
    {
        if (radius <= 0 || bounds.Width <= 0 || bounds.Height <= 0) return;

        var rect = new SKRect(bounds.X, bounds.Y, bounds.X + bounds.Width, bounds.Y + bounds.Height);

        using SKImage? snapshot = SnapshotUnder(rect, out SKRect taken);
        if (snapshot is null) return;

        using var filter = SKImageFilter.CreateBlur(radius / 2f, radius / 2f, SKShaderTileMode.Clamp);
        using var paint = new SKPaint { ImageFilter = filter };

        _canvas.Save();
        _canvas.ClipRect(rect);

        // the snapshot is drawn back on its own place, but blurred now
        _canvas.DrawImage(snapshot, taken, SKSamplingOptions.Default, paint);

        _canvas.Restore();
    }

    #region Layer capture

    public override bool SupportsLayerCapture => true;

    public override void BeginCapture(Rectangle bounds)
    {
        int width = (int)MathF.Ceiling(bounds.Width);
        int height = (int)MathF.Ceiling(bounds.Height);

        if (width <= 0 || height <= 0)
        {
            // nothing to capture, but a frame must be pushed onto the stack:
            // EndCapture must find a pair for its Begin
            _captures.Push(new CaptureFrame(null, _canvas, bounds));
            return;
        }

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        SKSurface? surface = SKSurface.Create(info);

        _captures.Push(new CaptureFrame(surface, _canvas, bounds));

        if (surface is null) return;

        // the element is drawn in its usual coordinates, while the surface starts
        // at zero — move its origin to the area's top-left corner
        surface.Canvas.Translate(-bounds.X, -bounds.Y);

        _canvas = surface.Canvas;
    }

    public override LayerCapture? EndCapture()
    {
        if (_captures.Count == 0)
        {
            ZfContract.Fail("EndCapture without a matching BeginCapture.");
            return null;
        }

        CaptureFrame frame = _captures.Pop();
        _canvas = frame.Previous;

        if (frame.Surface is null) return null;

        SKImage? image = frame.Surface.Snapshot();
        frame.Surface.Dispose();

        return image is null ? null : new SkiaLayerCapture(image, frame.Bounds);
    }

    public override void DrawCapture(
        LayerCapture capture,
        Rectangle target,
        Rectangle? sourceClip = null,
        ColorChannels channels = ColorChannels.All,
        CaptureBlend blend = CaptureBlend.Normal,
        float opacity = 1f)
    {
        if (capture is not SkiaLayerCapture skia) return;
        if (opacity <= 0f || target.Width <= 0 || target.Height <= 0) return;

        // a brush with a filter doesn't go into the pool: the pool holds only plain fills
        using var paint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, (byte)Math.Clamp(opacity * 255f, 0, 255)),
            BlendMode = blend == CaptureBlend.Screen ? SKBlendMode.Screen : SKBlendMode.SrcOver,
        };

        SKColorFilter? filter = ChannelFilter(channels);
        if (filter is not null) paint.ColorFilter = filter;

        if (sourceClip is { } clip)
        {
            // the slice's coordinates are inside the capture, so from its corner
            var source = new SKRect(
                clip.X - skia.Bounds.X,
                clip.Y - skia.Bounds.Y,
                clip.X - skia.Bounds.X + clip.Width,
                clip.Y - skia.Bounds.Y + clip.Height);

            _canvas.DrawImage(
                skia.Image, source,
                new SKRect(target.X, target.Y, target.X + target.Width, target.Y + target.Height),
                SKSamplingOptions.Default, paint);
        }
        else
        {
            _canvas.DrawImage(
                skia.Image,
                new SKRect(target.X, target.Y, target.X + target.Width, target.Y + target.Height),
                SKSamplingOptions.Default, paint);
        }

        filter?.Dispose();
    }

    /// <summary>A matrix that suppresses the unneeded channels. Alpha is not touched,
    /// otherwise transparent places would become visible.</summary>
    private static SKColorFilter? ChannelFilter(ColorChannels channels)
    {
        if (channels == ColorChannels.All) return null;

        float r = channels is ColorChannels.Red or ColorChannels.Magenta or ColorChannels.Yellow ? 1f : 0f;
        float g = channels is ColorChannels.Green or ColorChannels.Cyan or ColorChannels.Yellow ? 1f : 0f;
        float b = channels is ColorChannels.Blue or ColorChannels.Cyan or ColorChannels.Magenta ? 1f : 0f;

        return SKColorFilter.CreateColorMatrix(
        [
            r, 0, 0, 0, 0,
            0, g, 0, 0, 0,
            0, 0, b, 0, 0,
            0, 0, 0, 1, 0,
        ]);
    }

    private sealed class SkiaLayerCapture(SKImage image, Rectangle bounds) : LayerCapture
    {
        public SKImage Image { get; } = image;

        public override Rectangle Bounds { get; } = bounds;

        public override void Dispose() => Image.Dispose();
    }

    #endregion

    public override void Skew(float sx, float sy)
    {
        // SKMatrix.CreateSkew sets the skew relative to the origin;
        // the pivot point is set by the caller through Translate
        _canvas.Concat(SKMatrix.CreateSkew(sx, sy));
    }

    public override void FillNoise(Rectangle bounds, float opacity)
    {
        if (opacity <= 0 || bounds.Width <= 0 || bounds.Height <= 0) return;

        // the shader is shared and lives until the process ends — it must not be taken into a using
        SKShader shader = NoiseShader;

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

    // the noise is generated once: the procedural texture is the same for all
    // elements, and recreating it every frame is too expensive.
    //
    // Exactly a field, not an expression-bodied property: with the arrow, every
    // access created a new shader, that is, the code did exactly what the line
    // above forbids. A shared instance is safe across threads too —
    // shaders in Skia are immutable.
    private static readonly SKShader NoiseShader =
        SKShader.CreatePerlinNoiseFractalNoise(0.8f, 0.8f, 2, 0f);

    public override void DrawReflection(Rectangle bounds, float heightRatio, float gap, float startOpacity)
    {
        if (heightRatio <= 0 || bounds.Width <= 0 || bounds.Height <= 0) return;

        var source = new SKRect(bounds.X, bounds.Y, bounds.X + bounds.Width, bounds.Y + bounds.Height);

        using SKImage? snapshot = SnapshotUnder(source, out SKRect taken);
        if (snapshot is null) return;

        float reflectionHeight = bounds.Height * Math.Clamp(heightRatio, 0f, 1f);
        float top = bounds.Y + bounds.Height + gap;

        var target = new SKRect(bounds.X, top, bounds.X + bounds.Width, top + reflectionHeight);

        // the fade goes from translucent to zero: the reflection must dissolve rather
        // than break off at the edge. It is drawn inside the mirrored coordinates below,
        // where target.Bottom lands right under the element and target.Top furthest
        // from it — so the opaque end is at target.Bottom. It used to be at target.Top,
        // and the reflection faded in the wrong direction: invisible right under the
        // element and brightest away from it
        using SKShader fade = SKShader.CreateLinearGradient(
            new SKPoint(target.Left, target.Bottom),
            new SKPoint(target.Left, target.Top),
            [
                new SKColor(255, 255, 255, (byte)Math.Clamp(startOpacity * 255f, 0, 255)),
                new SKColor(255, 255, 255, 0),
            ],
            null,
            SKShaderTileMode.Clamp);

        using var paint = new SKPaint { Shader = fade, BlendMode = SKBlendMode.DstIn };

        _canvas.Save();
        _canvas.ClipRect(target);

        // mirror vertically relative to the reflection's top edge
        _canvas.Translate(0, target.Top);
        _canvas.Scale(1, -1);
        _canvas.Translate(0, -target.Top - reflectionHeight);

        // only the part of the element actually taken is reflected, at its own
        // share of the reflection's height — a snapshot cut at the surface's edge
        // must not be stretched over the whole reflection
        float fromTop = (taken.Top - source.Top) / source.Height;
        float toTop = (taken.Bottom - source.Top) / source.Height;

        var flipped = new SKRect(
            taken.Left, target.Top + fromTop * reflectionHeight,
            taken.Right, target.Top + toTop * reflectionHeight);

        // the layer is needed so that the fade applies to the reflection,
        // not to what is already drawn under it
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

        // the direction is set by an angle: 0 — left to right, 90 — top to bottom
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

    public override void FillPolygon(ReadOnlySpan<Point> points, Color color)
    {
        if (points.Length < 3) return;

        using var builder = new SKPathBuilder();
        builder.MoveTo(points[0].X, points[0].Y);

        for (int i = 1; i < points.Length; i++)
            builder.LineTo(points[i].X, points[i].Y);

        builder.Close();

        using SKPath path = builder.Detach();

        _canvas.DrawPath(path, FillPaint(color));
    }

    private readonly record struct CaptureFrame(
        SKSurface? Surface, SKCanvas Previous, Rectangle Bounds);
}