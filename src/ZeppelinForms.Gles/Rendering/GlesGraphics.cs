using ZeppelinForms.Core.Text;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Helpers;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Gles.Geometry;

namespace ZeppelinForms.Gles.Rendering;

/// <summary>
/// <see cref="Graphics"/> on OpenGL ES: rectangles, ellipses, lines and shadows as quads
/// whose coverage the shader computes, paths through a coverage mask made on the CPU,
/// pictures and gradients as textures, layers in framebuffers.
/// </summary>
/// <remarks>
/// <para>
/// The transform is applied on the CPU, to the quads' corners: the GPU sees device pixels,
/// and a batch mixes whatever transforms its primitives had. A quad also carries its
/// coordinates in its own shape's space, where the distance to its edge is measured, so a
/// rotated or skewed rounded rectangle is still exact and antialiased.
/// </para>
/// <para>
/// A rectangular clip under a transform without rotation is a scissor. A rounded one — a
/// card, a circle of an avatar — is computed in the shader, antialiased, two of them at
/// once; a third, or one under a rotation, goes into the stencil, with hard edges.
/// </para>
/// <para>
/// Text is drawn from a glyph atlas, its glyphs rasterized from the fonts' outlines; see
/// the text part of the class. Blurs, backdrops, reflections and blend modes other than
/// normal come in the next steps; until then a blurred layer is drawn sharp and a blended
/// one normally.
/// </para>
/// </remarks>
internal sealed partial class GlesGraphics : Graphics
{
    /// <summary>How far a flattened curve may be off the curve, in device pixels.</summary>
    private const float FlattenTolerance = 0.2f;

    /// <summary>The largest mask rasterized at once; larger paths go in tiles.</summary>
    private const int MaxTile = 1024;

    private readonly GlesRenderer _renderer;

    private readonly Stack<State> _stack = new();
    private readonly List<Vertex[]> _stencilClips = [];
    private bool _stencilDirty;

    private readonly VectorPath _path = new();
    private readonly Polylines _lines = new();
    private readonly Polylines _outline = new();
    private readonly Stroker _stroker = new();
    private readonly Rasterizer _rasterizer = new();
    private byte[] _coverage = new byte[64 * 64];

    private State _state;

    private struct State
    {
        public Affine Matrix;
        public PixelRect Scissor;
        public ClipShape Clip0;
        public ClipShape Clip1;
        public int StencilDepth;

        /// <summary>The layer this level opened, composited when it is restored.</summary>
        public LayerFrame? Layer;
    }

    private sealed record LayerFrame(
        LayerTexture? Texture, RenderTarget Previous, PixelRect Bounds, float Opacity, float Saturation);

    public GlesGraphics(GlesRenderer renderer)
    {
        _renderer = renderer;
    }

    /// <summary>A frame begins: no transform, no clip, nothing saved — whatever a broken
    /// frame left behind.</summary>
    public void Begin(RenderTarget target)
    {
        _stack.Clear();
        _stencilClips.Clear();
        _stencilDirty = false;

        TextEffects = default;
        TextDirection = default;

        _state = new State
        {
            Matrix = Affine.Identity,
            Scissor = target.Bounds,
            Clip0 = ClipShape.None,
            Clip1 = ClipShape.None,
        };
    }

    /// <summary>The frame ends: layers a broken frame left open are put down, so their
    /// textures go back.</summary>
    public void End()
    {
        while (_stack.Count > 0) Restore();
    }

    #region State

    public override void Save()
    {
        _stack.Push(_state);
        _state.Layer = null;
    }

    public override void Restore()
    {
        if (_stack.Count == 0) return;

        LayerFrame? layer = _state.Layer;
        int depth = _state.StencilDepth;

        _state = _stack.Pop();

        if (_state.StencilDepth != depth)
        {
            _stencilClips.RemoveRange(_state.StencilDepth, _stencilClips.Count - _state.StencilDepth);

            // fewer clips: the counts in the stencil are made anew before the next draw
            _stencilDirty = true;
        }

        if (layer is not null) EndLayer(layer);
    }

    public override void Translate(float dx, float dy) =>
        _state.Matrix = _state.Matrix.Then(Affine.Translation(dx, dy));

    public override void Scale(float sx, float sy) =>
        _state.Matrix = _state.Matrix.Then(Affine.Scaling(sx, sy));

    public override void Rotate(float degrees) =>
        _state.Matrix = _state.Matrix.Then(Affine.Rotation(degrees));

    public override void Skew(float sx, float sy) =>
        _state.Matrix = _state.Matrix.Then(Affine.Skewing(sx, sy));

    public override Rectangle AlignToPixels(Rectangle rect)
    {
        Affine m = _state.Matrix;

        // only a scale and an offset keep the edges on pixel rows and columns
        if (!m.IsPositiveScaleTranslate) return rect;

        float left = MathF.Round(rect.X * m.A + m.E);
        float top = MathF.Round(rect.Y * m.D + m.F);
        float right = MathF.Round(rect.Right * m.A + m.E);
        float bottom = MathF.Round(rect.Bottom * m.D + m.F);

        // a rectangle thinner than a pixel would vanish: it stays where it was
        if (right <= left || bottom <= top) return rect;

        return new Rectangle(
            new Point((left - m.E) / m.A, (top - m.F) / m.D),
            new Size((right - left) / m.A, (bottom - top) / m.D));
    }

    #endregion

    #region Clips

    public override void ClipRect(Rectangle bounds)
    {
        Affine m = _state.Matrix;

        if (m.IsAxisAligned)
        {
            // a clip without antialiasing, as Skia's: the edges rounded to whole pixels
            _state.Scissor = _state.Scissor.Intersect(PixelRect.Round(m.MapBounds(bounds)));
            return;
        }

        AddStencilClip(bounds, CornerRadius.Zero, Shaders.ShapeRoundRect);
    }

    public override void ClipRoundRect(Rectangle rect, CornerRadius radius)
    {
        if (radius.IsZero)
        {
            ClipRect(rect);
            return;
        }

        ClipRounded(rect, Clamp(radius, rect), Shaders.ShapeRoundRect);
    }

    public override void ClipCircle(Point center, float radius)
    {
        radius = MathF.Max(0, radius);

        var rect = new Rectangle(center.X - radius, center.Y - radius, radius * 2, radius * 2);
        ClipRounded(rect, new CornerRadius(radius), Shaders.ShapeEllipse);
    }

    private void ClipRounded(Rectangle rect, CornerRadius radius, int shape)
    {
        Affine m = _state.Matrix;

        // the analytic clip is a rounded rectangle of circular corners in device pixels:
        // a scale the same both ways keeps the corners circles
        if (m.IsPositiveScaleTranslate && MathF.Abs(m.A - m.D) < 1e-3f * m.A && (_state.Clip0.IsNone || _state.Clip1.IsNone))
        {
            Rectangle device = m.MapBounds(rect);
            float s = m.A;

            var clip = new ClipShape(
                device.X, device.Y, device.Right, device.Bottom,
                radius.TopLeft * s, radius.TopRight * s, radius.BottomRight * s, radius.BottomLeft * s);

            if (_state.Clip0.IsNone) _state.Clip0 = clip;
            else _state.Clip1 = clip;

            _state.Scissor = _state.Scissor.Intersect(PixelRect.Outer(device));
            return;
        }

        AddStencilClip(rect, radius, shape);
    }

    /// <summary>A clip into the stencil: hard-edged, but under any transform and any number
    /// of them nested.</summary>
    private void AddStencilClip(Rectangle rect, CornerRadius radius, int shape)
    {
        _state.Scissor = _state.Scissor.Intersect(PixelRect.Outer(_state.Matrix.MapBounds(rect)));

        Vertex[] quad = new Vertex[4];
        WriteShapeQuad(quad, rect, shape, Shaders.PaintColor, radius, 0, 0xFFFFFFFF, margin: 0);

        // the clips of this level and every one under it: the restore truncates the list
        _stencilClips.RemoveRange(_state.StencilDepth, _stencilClips.Count - _state.StencilDepth);
        _stencilClips.Add(quad);
        _state.StencilDepth++;

        _stencilDirty = true;
    }

    /// <summary>Put the stencil clips into the stencil of the current target: each one
    /// counts up where all those before it did.</summary>
    private void EnsureStencil()
    {
        if (!_stencilDirty) return;

        _stencilDirty = false;

        if (_state.StencilDepth == 0) return;

        _renderer.ClearStencil();

        PixelRect everywhere = _renderer.Target.Bounds;

        for (int i = 0; i < _state.StencilDepth; i++)
        {
            var state = new BatchState(0, false, BlendKind.Normal, everywhere, ClipShape.None, ClipShape.None, StencilUse.Write, i);

            Span<Vertex> quad = _renderer.AddQuad(state);
            _stencilClips[i].CopyTo(quad);

            // each level counts on the one before: drawn one at a time
            _renderer.Flush();
        }
    }

    private BatchState Batch(uint image = 0, bool repeat = false, BlendKind blend = BlendKind.Normal) =>
        new(
            image, repeat, blend, _state.Scissor, _state.Clip0, _state.Clip1,
            _state.StencilDepth > 0 ? StencilUse.Test : StencilUse.None,
            _state.StencilDepth);

    /// <summary>Room for a quad in the current state, or none when the clip leaves nothing
    /// of the device bounds it covers.</summary>
    private Span<Vertex> Quad(Rectangle deviceBounds, uint image = 0, bool repeat = false, BlendKind blend = BlendKind.Normal)
    {
        if (_state.Scissor.IsEmpty) return [];

        if (_state.Scissor.Intersect(PixelRect.Outer(deviceBounds)).IsEmpty) return [];

        EnsureStencil();

        return _renderer.AddQuad(Batch(image, repeat, blend));
    }

    #endregion

    #region Layers

    public override void SaveLayer(float opacity) => SaveLayer(opacity, 1f);

    public override void SaveLayer(float opacity, BlendMode blendMode) =>
        // TODO(0.19): the blend modes, through a copy of what is under the layer
        SaveLayer(opacity, 1f);

    public override void SaveDisabledLayer(float opacity, float desaturation) =>
        SaveLayer(opacity, 1f - Math.Clamp(desaturation, 0f, 1f));

    public override void SaveBlurLayer(float radius) =>
        // TODO(0.19): the blur, in two passes over the layer
        SaveLayer(1f, 1f);

    private void SaveLayer(float opacity, float saturation)
    {
        opacity = Math.Clamp(opacity, 0f, 1f);

        Save();

        // a layer of full opacity and color changes nothing: drawn straight through
        if (opacity >= 1f && saturation >= 1f) return;

        RenderTarget previous = _renderer.Target;
        PixelRect bounds = _state.Scissor.Intersect(previous.Bounds);

        if (bounds.IsEmpty)
        {
            // nothing of it can show: whatever is drawn in it is clipped away
            _state.Layer = new LayerFrame(null, previous, bounds, opacity, saturation);
            _state.Scissor = bounds;
            return;
        }

        LayerTexture texture = _renderer.AcquireLayer(bounds.Width, bounds.Height);
        var target = new RenderTarget(texture.Framebuffer, bounds.Width, bounds.Height, bounds.X, bounds.Y);

        _state.Layer = new LayerFrame(texture, previous, bounds, opacity, saturation);

        _renderer.SetTarget(target);
        _renderer.Clear(default, bounds);

        // the stencil clips go into the layer's own stencil
        if (_state.StencilDepth > 0) _stencilDirty = true;
    }

    private void EndLayer(LayerFrame layer)
    {
        if (layer.Texture is not { } texture) return;

        _renderer.SetTarget(layer.Previous);

        // back on the target below: its stencil holds its own clips, made anew
        if (_state.StencilDepth > 0) _stencilDirty = true;

        PixelRect b = layer.Bounds;
        var device = new Rectangle(b.X, b.Y, b.Width, b.Height);

        Span<Vertex> quad = Quad(device, texture.Texture);

        if (!quad.IsEmpty)
        {
            // the layer was drawn into the lower left of its texture, GL's rows from the bottom
            float u1 = (float)b.Width / texture.Width;
            float vTop = (float)b.Height / texture.Height;

            byte o = (byte)(layer.Opacity * 255f + 0.5f);
            uint color = Pack(o, o, o, o);
            float kind = Shaders.Kind(Shaders.ShapeNone, Shaders.PaintLayer);

            quad[0] = DeviceVertex(b.X, b.Y, color, kind, 0, vTop, layer.Saturation);
            quad[1] = DeviceVertex(b.Right, b.Y, color, kind, u1, vTop, layer.Saturation);
            quad[2] = DeviceVertex(b.Right, b.Bottom, color, kind, u1, 0, layer.Saturation);
            quad[3] = DeviceVertex(b.X, b.Bottom, color, kind, 0, 0, layer.Saturation);
        }

        // the composite is drawn before any later layer can take the texture: taking one
        // changes the target, and that flushes
        GlesRenderer.ReleaseLayer(texture);
    }

    #endregion

    #region Shapes

    public override void Clear(Color color)
    {
        PixelRect area = _state.Scissor.Intersect(_renderer.Target.Bounds);
        if (area.IsEmpty) return;

        if (_state.Clip0.IsNone && _state.Clip1.IsNone && _state.StencilDepth == 0)
        {
            _renderer.Clear(color, area);
            return;
        }

        // a clip of the shader or the stencil: the color replaces what is there inside it
        Affine saved = _state.Matrix;
        _state.Matrix = Affine.Identity;

        var rect = new Rectangle(area.X, area.Y, area.Width, area.Height);
        Span<Vertex> quad = Quad(rect, blend: BlendKind.Replace);

        if (!quad.IsEmpty)
            WriteShapeQuad(quad, rect, Shaders.ShapeNone, Shaders.PaintColor, CornerRadius.Zero, 0, Premultiply(color), margin: 0);

        _state.Matrix = saved;
    }

    public override void FillRectangle(Rectangle rect, Color color) =>
        Shape(rect, Shaders.ShapeRoundRect, CornerRadius.Zero, 0, color);

    public override void DrawRectangle(Rectangle rect, Color color, float width) =>
        Shape(rect, Shaders.ShapeRoundRect, CornerRadius.Zero, StrokeWidth(width), color);

    public override void FillRoundRectangle(Rectangle rect, CornerRadius radius, Color color) =>
        Shape(rect, Shaders.ShapeRoundRect, Clamp(radius, rect), 0, color);

    public override void DrawRoundRectangle(Rectangle rect, CornerRadius radius, Color color, float width) =>
        Shape(rect, Shaders.ShapeRoundRect, Clamp(radius, rect), StrokeWidth(width), color);

    public override void FillEllipse(Rectangle rect, Color color) => Ellipse(rect, 0, color);

    public override void DrawEllipse(Rectangle rect, Color color, float width) => Ellipse(rect, StrokeWidth(width), color);

    private void Ellipse(Rectangle rect, float stroke, Color color)
    {
        // a circle is a rounded rectangle of half its size: exact, where the ellipse's
        // distance is approximate
        if (MathF.Abs(rect.Width - rect.Height) < 1e-4f)
            Shape(rect, Shaders.ShapeRoundRect, new CornerRadius(rect.Width / 2f), stroke, color);
        else
            Shape(rect, Shaders.ShapeEllipse, CornerRadius.Zero, stroke, color);
    }

    private void Shape(Rectangle rect, int shape, CornerRadius radius, float stroke, Color color)
    {
        if (color.A == 0 || rect.Width < 0 || rect.Height < 0) return;
        if (stroke == 0 && (rect.Width == 0 || rect.Height == 0)) return;

        float margin = stroke / 2f + 1.5f / _state.Matrix.MinScale;

        Rectangle device = _state.Matrix.MapBounds(Inflate(rect, margin));
        Span<Vertex> quad = Quad(device);

        if (!quad.IsEmpty)
            WriteShapeQuad(quad, rect, shape, Shaders.PaintColor, radius, stroke, Premultiply(color), margin);
    }

    /// <summary>The four corners of a shape's quad, the shape centered in its own space,
    /// the quad larger than it by the margin its antialiasing and stroke reach into.</summary>
    private void WriteShapeQuad(
        Span<Vertex> quad, Rectangle rect, int shape, int paint, CornerRadius radius, float stroke, uint color, float margin,
        float e0 = 0, float e1 = 0, float e2 = 0, float e3 = 0)
    {
        Affine m = _state.Matrix;

        float hw = rect.Width / 2f;
        float hh = rect.Height / 2f;
        float cx = rect.X + hw;
        float cy = rect.Y + hh;

        float x0 = -hw - margin, x1 = hw + margin;
        float y0 = -hh - margin, y1 = hh + margin;

        float kind = Shaders.Kind(shape, paint);

        quad[0] = ShapeVertex(m, cx, cy, x0, y0);
        quad[1] = ShapeVertex(m, cx, cy, x1, y0);
        quad[2] = ShapeVertex(m, cx, cy, x1, y1);
        quad[3] = ShapeVertex(m, cx, cy, x0, y1);

        Vertex ShapeVertex(in Affine matrix, float centerX, float centerY, float lx, float ly)
        {
            Point p = matrix.Map(centerX + lx, centerY + ly);

            return new Vertex
            {
                X = p.X,
                Y = p.Y,
                LocalX = lx,
                LocalY = ly,
                Color = color,
                HalfWidth = hw,
                HalfHeight = hh,
                Stroke = stroke,
                Kind = kind,
                R0 = radius.TopLeft,
                R1 = radius.TopRight,
                R2 = radius.BottomRight,
                R3 = radius.BottomLeft,
                E0 = e0,
                E1 = e1,
                E2 = e2,
                E3 = e3,
            };
        }
    }

    public override void DrawLine(Point from, Point to, Color color, float width)
    {
        if (color.A == 0) return;

        width = StrokeWidth(width);

        float dx = to.X - from.X, dy = to.Y - from.Y;
        float length = MathF.Sqrt(dx * dx + dy * dy);

        // along the line and across it; a point is a dot of the round cap
        float ux = length > 0 ? dx / length : 1, uy = length > 0 ? dy / length : 0;
        float vx = -uy, vy = ux;

        float half = width / 2f;
        float hw = length / 2f + half;
        float hh = half;
        float margin = 1.5f / _state.Matrix.MinScale;

        float cx = (from.X + to.X) / 2f, cy = (from.Y + to.Y) / 2f;

        Affine m = _state.Matrix;
        uint packed = Premultiply(color);
        float kind = Shaders.Kind(Shaders.ShapeRoundRect, Shaders.PaintColor);

        float ex = hw + margin, ey = hh + margin;

        Point p0 = m.Map(cx - ux * ex - vx * ey, cy - uy * ex - vy * ey);
        Point p1 = m.Map(cx + ux * ex - vx * ey, cy + uy * ex - vy * ey);
        Point p2 = m.Map(cx + ux * ex + vx * ey, cy + uy * ex + vy * ey);
        Point p3 = m.Map(cx - ux * ex + vx * ey, cy - uy * ex + vy * ey);

        Rectangle device = Bounds(p0, p1, p2, p3);
        Span<Vertex> quad = Quad(device);
        if (quad.IsEmpty) return;

        // the capsule: a rounded rectangle along the line, its radius the half-width
        quad[0] = Line(p0, -ex, -ey);
        quad[1] = Line(p1, ex, -ey);
        quad[2] = Line(p2, ex, ey);
        quad[3] = Line(p3, -ex, ey);

        Vertex Line(Point p, float lx, float ly) => new()
        {
            X = p.X,
            Y = p.Y,
            LocalX = lx,
            LocalY = ly,
            Color = packed,
            HalfWidth = hw,
            HalfHeight = hh,
            Kind = kind,
            R0 = half,
            R1 = half,
            R2 = half,
            R3 = half,
        };
    }

    public override void DrawShadow(Rectangle rect, BoxShadow shadow)
    {
        if (shadow.Color.A == 0) return;

        var shadowRect = new Rectangle(
            rect.X + shadow.OffsetX - shadow.Spread,
            rect.Y + shadow.OffsetY - shadow.Spread,
            rect.Width + shadow.Spread * 2,
            rect.Height + shadow.Spread * 2);

        if (shadowRect.Width <= 0 || shadowRect.Height <= 0) return;

        if (shadow.Blur <= 0)
        {
            FillRectangle(shadowRect, shadow.Color);
            return;
        }

        // sigma = blur / 2: the blur radius as CSS means it, as Skia's backend has it
        float sigma = shadow.Blur / 2f;
        float margin = sigma * 3f + 1f / _state.Matrix.MinScale;

        Rectangle device = _state.Matrix.MapBounds(Inflate(shadowRect, margin));
        Span<Vertex> quad = Quad(device);

        if (!quad.IsEmpty)
        {
            WriteShapeQuad(
                quad, shadowRect, Shaders.ShapeShadow, Shaders.PaintColor,
                new CornerRadius(sigma, 0, 0, 0), 0, Premultiply(shadow.Color), margin);
        }
    }

    public override void FillGradient(Rectangle bounds, CornerRadius radius, GradientStop[] stops, float angle)
    {
        if (stops.Length == 0 || bounds.Width <= 0 || bounds.Height <= 0) return;

        if (stops.Length == 1)
        {
            FillRoundRectangle(bounds, radius, stops[0].Color);
            return;
        }

        // the direction is set by an angle: 0 — left to right, 90 — top to bottom; the
        // ends are on the rectangle's edges, as in Skia's backend
        float radians = angle * MathF.PI / 180f;
        float dx = MathF.Cos(radians) * bounds.Width / 2f;
        float dy = MathF.Sin(radians) * bounds.Height / 2f;
        float d2 = dx * dx + dy * dy;

        float row = _renderer.GradientRow(stops);

        float margin = 1.5f / _state.Matrix.MinScale;
        Rectangle device = _state.Matrix.MapBounds(Inflate(bounds, margin));
        Span<Vertex> quad = Quad(device);
        if (quad.IsEmpty) return;

        WriteShapeQuad(quad, bounds, Shaders.ShapeRoundRect, Shaders.PaintGradient, Clamp(radius, bounds), 0, 0xFFFFFFFF, margin);

        // the position along the gradient is affine in the shape's space: one value a
        // corner, interpolated exactly between them
        for (int i = 0; i < 4; i++)
        {
            ref Vertex v = ref quad[i];

            v.E0 = d2 > 0 ? 0.5f + (v.LocalX * dx + v.LocalY * dy) / (2f * d2) : 0f;
            v.E1 = row;
        }
    }

    public override void FillNoise(Rectangle bounds, float opacity)
    {
        if (opacity <= 0 || bounds.Width <= 0 || bounds.Height <= 0) return;

        Rectangle device = _state.Matrix.MapBounds(bounds);
        Span<Vertex> quad = Quad(device);
        if (quad.IsEmpty) return;

        byte o = (byte)(Math.Clamp(opacity, 0f, 1f) * 255f + 0.5f);

        WriteShapeQuad(quad, bounds, Shaders.ShapeNone, Shaders.PaintNoise, CornerRadius.Zero, 0, Pack(o, o, o, o), margin: 0, e0: 0.8f);

        // the noise is fixed to the canvas, not to the rectangle: its coordinates are
        // the canvas's own
        float cx = bounds.X + bounds.Width / 2f, cy = bounds.Y + bounds.Height / 2f;

        for (int i = 0; i < 4; i++)
        {
            quad[i].LocalX += cx;
            quad[i].LocalY += cy;
        }
    }

    #endregion

    #region Paths

    public override void FillPolygon(ReadOnlySpan<Point> points, Color color)
    {
        if (points.Length < 3 || color.A == 0) return;

        _lines.Clear();
        _lines.BeginContour();

        Affine m = _state.Matrix;
        foreach (Point p in points) _lines.Add(m.Map(p));

        _lines.EndContour(closed: true);

        FillDevice(_lines, Premultiply(color), FillRule.NonZero);
    }

    public override void DrawPolyline(ReadOnlySpan<Point> points, Color color, float width)
    {
        if (points.Length < 2 || color.A == 0) return;

        _lines.Clear();
        _lines.BeginContour();
        foreach (Point p in points) _lines.Add(p);
        _lines.EndContour(closed: false);

        // round joins: without them the corner of a check mark looks chopped
        StrokeLocal(_lines, StrokeWidth(width), LineCap.Round, LineJoin.Round, Premultiply(color));
    }

    public override void DrawBeziers(ReadOnlySpan<Point> points, Color color, float width)
    {
        int curves = Bezier.CountCurves(points, nameof(points));
        if (curves == 0 || color.A == 0) return;

        _path.Clear();
        _path.MoveTo(points[0]);

        for (int i = 0; i < curves; i++)
            _path.CubicTo(points[i * 3 + 1], points[i * 3 + 2], points[i * 3 + 3]);

        StrokePath(_path, StrokeWidth(width), LineCap.Round, LineJoin.Round, Premultiply(color));
    }

    public override void DrawArc(Rectangle rect, float startAngle, float sweepAngle, Color color, float width)
    {
        if (color.A == 0 || sweepAngle == 0) return;

        if (MathF.Abs(sweepAngle) >= 360f)
        {
            DrawEllipse(rect, color, width);
            return;
        }

        _path.Clear();
        _path.AddArc(
            rect.Center, rect.Width / 2f, rect.Height / 2f,
            startAngle * MathF.PI / 180f, sweepAngle * MathF.PI / 180f, moveTo: true);

        StrokePath(_path, StrokeWidth(width), LineCap.Round, LineJoin.Miter, Premultiply(color));
    }

    public override void FillPie(Rectangle rect, float startAngle, float sweepAngle, Color color)
    {
        if (color.A == 0 || sweepAngle == 0) return;

        if (MathF.Abs(sweepAngle) >= 360f)
        {
            FillEllipse(rect, color);
            return;
        }

        _path.Clear();
        _path.MoveTo(rect.Center);
        _path.AddArc(
            rect.Center, rect.Width / 2f, rect.Height / 2f,
            startAngle * MathF.PI / 180f, sweepAngle * MathF.PI / 180f, moveTo: false);
        _path.Close();

        FillPath(_path, Premultiply(color));
    }

    public override void DrawSvgPath(string pathData, Rectangle rect, Color color, float strokeWidth = 0f)
    {
        if (string.IsNullOrEmpty(pathData) || color.A == 0) return;

        VectorPath path = SvgPathParser.Get(pathData);
        if (path.IsEmpty) return;

        Rectangle bounds = path.ControlBounds;

        // a stroke reaches half its width past the path's own bounds, and it is scaled
        // with the path: fitted by the bare bounds, a stroked glyph would lose its edges
        if (strokeWidth > 0) bounds = Inflate(bounds, strokeWidth / 2f);

        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        // fitted into the rectangle, keeping the proportions, centered
        float scale = MathF.Min(rect.Width / bounds.Width, rect.Height / bounds.Height);
        float dx = rect.X + (rect.Width - bounds.Width * scale) / 2f - bounds.X * scale;
        float dy = rect.Y + (rect.Height - bounds.Height * scale) / 2f - bounds.Y * scale;

        Affine saved = _state.Matrix;
        _state.Matrix = saved.Then(Affine.Translation(dx, dy)).Then(Affine.Scaling(scale, scale));

        if (strokeWidth > 0)
            StrokePath(path, strokeWidth, LineCap.Round, LineJoin.Round, Premultiply(color));
        else
            FillPath(path, Premultiply(color));

        _state.Matrix = saved;
    }

    private void FillPath(VectorPath path, uint color)
    {
        _lines.Clear();
        path.Flatten(_state.Matrix, FlattenTolerance, _lines);

        FillDevice(_lines, color, FillRule.NonZero);
    }

    private void StrokePath(VectorPath path, float width, LineCap cap, LineJoin join, uint color)
    {
        // flattened in the path's own units, finely enough for the device: the stroke is
        // made there, where its width is, and mapped after — a skew widens it as it should
        _lines.Clear();
        path.Flatten(Affine.Identity, FlattenTolerance / _state.Matrix.MeanScale, _lines);

        StrokeLocal(_lines, width, cap, join, color);
    }

    private void StrokeLocal(Polylines lines, float width, LineCap cap, LineJoin join, uint color)
    {
        _outline.Clear();
        _stroker.Stroke(lines, width, cap, join, Stroker.DefaultMiterLimit, FlattenTolerance / _state.Matrix.MeanScale, _outline);
        _outline.Transform(_state.Matrix);

        FillDevice(_outline, color, FillRule.NonZero);
    }

    /// <summary>Contours in device pixels filled through masks: rasterized in tiles over
    /// the part the clip leaves, each tile into the atlas and drawn as a quad.</summary>
    private void FillDevice(Polylines contours, uint color, FillRule rule)
    {
        if (contours.PointCount == 0) return;

        PixelRect area = PixelRect.Outer(contours.Bounds);
        area = new PixelRect(area.X - 1, area.Y - 1, area.Width + 2, area.Height + 2);
        area = area.Intersect(_state.Scissor).Intersect(_renderer.Target.Bounds);

        if (area.IsEmpty) return;

        float kind = Shaders.Kind(Shaders.ShapeNone, Shaders.PaintMask);
        float atlas = _renderer.MaskAtlasSize;

        for (int ty = area.Y; ty < area.Bottom; ty += MaxTile)
        {
            for (int tx = area.X; tx < area.Right; tx += MaxTile)
            {
                int tw = Math.Min(MaxTile, area.Right - tx);
                int th = Math.Min(MaxTile, area.Bottom - ty);
                int size = tw * th;

                if (_coverage.Length < size) _coverage = new byte[Math.Max(size, _coverage.Length * 2)];

                Span<byte> coverage = _coverage.AsSpan(0, size);
                _rasterizer.Rasterize(contours, tx, ty, tw, th, rule, coverage);

                // a tile the path misses — an L-shaped chart line's empty corner
                if (!coverage.ContainsAnyExcept((byte)0)) continue;

                if (!_renderer.UploadMask(tw, th, coverage, out int ax, out int ay)) continue;

                var device = new Rectangle(tx, ty, tw, th);
                Span<Vertex> quad = Quad(device);
                if (quad.IsEmpty) continue;

                float u0 = ax / atlas, v0 = ay / atlas;
                float u1 = (ax + tw) / atlas, v1 = (ay + th) / atlas;

                quad[0] = DeviceVertex(tx, ty, color, kind, u0, v0);
                quad[1] = DeviceVertex(tx + tw, ty, color, kind, u1, v0);
                quad[2] = DeviceVertex(tx + tw, ty + th, color, kind, u1, v1);
                quad[3] = DeviceVertex(tx, ty + th, color, kind, u0, v1);
            }
        }
    }

    #endregion

    #region Pictures

    public override void DrawImage(Rectangle rect, Image image, ImageFlip flip = ImageFlip.None, ImageLayout layout = ImageLayout.Stretch)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;

        ImageTexture texture = _renderer.Texture(image);

        float w = image.Width, h = image.Height;

        // the target and the part of the picture in it, in local units and texture units
        Rectangle target;
        float u0 = 0, v0 = 0, u1 = 1, v1 = 1;
        bool repeat = false;

        switch (layout)
        {
            case ImageLayout.Tile:
                // the tiling starts from the area's corner
                target = rect;
                u1 = rect.Width / w;
                v1 = rect.Height / h;
                repeat = true;
                break;

            case ImageLayout.None:
                target = new Rectangle(rect.X, rect.Y, w, h);
                break;

            case ImageLayout.Center:
                target = new Rectangle(rect.X + (rect.Width - w) / 2f, rect.Y + (rect.Height - h) / 2f, w, h);
                break;

            case ImageLayout.Zoom:
                {
                    float scale = MathF.Min(rect.Width / w, rect.Height / h);
                    float zw = w * scale, zh = h * scale;
                    target = new Rectangle(rect.X + (rect.Width - zw) / 2f, rect.Y + (rect.Height - zh) / 2f, zw, zh);
                    break;
                }

            default:
                target = rect;
                break;
        }

        // the picture never leaves its rectangle: cut to it, the texture coordinates with it
        if (!repeat)
        {
            float left = MathF.Max(target.X, rect.X), top = MathF.Max(target.Y, rect.Y);
            float right = MathF.Min(target.Right, rect.Right), bottom = MathF.Min(target.Bottom, rect.Bottom);

            if (right <= left || bottom <= top) return;

            u0 = (left - target.X) / target.Width;
            u1 = (right - target.X) / target.Width;
            v0 = (top - target.Y) / target.Height;
            v1 = (bottom - target.Y) / target.Height;

            target = new Rectangle(left, top, right - left, bottom - top);
        }

        // flipped around the area's center, as the picture is mirrored in it
        bool flipX = flip is ImageFlip.Horizontal or ImageFlip.Both;
        bool flipY = flip is ImageFlip.Vertical or ImageFlip.Both;

        float cx = rect.X + rect.Width / 2f, cy = rect.Y + rect.Height / 2f;

        float x0 = target.X, x1 = target.Right, y0 = target.Y, y1 = target.Bottom;

        if (flipX) (x0, x1) = (2 * cx - x0, 2 * cx - x1);
        if (flipY) (y0, y1) = (2 * cy - y0, 2 * cy - y1);

        Affine m = _state.Matrix;

        Point p0 = m.Map(x0, y0), p1 = m.Map(x1, y0), p2 = m.Map(x1, y1), p3 = m.Map(x0, y1);

        Span<Vertex> quad = Quad(Bounds(p0, p1, p2, p3), texture.Id, repeat);
        if (quad.IsEmpty) return;

        float kind = Shaders.Kind(Shaders.ShapeNone, Shaders.PaintImage);

        quad[0] = DeviceVertex(p0.X, p0.Y, 0xFFFFFFFF, kind, u0, v0);
        quad[1] = DeviceVertex(p1.X, p1.Y, 0xFFFFFFFF, kind, u1, v0);
        quad[2] = DeviceVertex(p2.X, p2.Y, 0xFFFFFFFF, kind, u1, v1);
        quad[3] = DeviceVertex(p3.X, p3.Y, 0xFFFFFFFF, kind, u0, v1);
    }

    public override void BlurBackdrop(Rectangle bounds, float radius)
    {
        // TODO(0.19): a copy of the target under the bounds, blurred and drawn back
    }

    public override void DrawReflection(Rectangle bounds, float heightRatio, float gap, float startOpacity)
    {
        // TODO(0.19): a copy of the target under the bounds, mirrored and faded
    }

    #endregion

    #region Helpers

    /// <summary>Skia's hairline: a width of 0 strokes one device pixel.</summary>
    private float StrokeWidth(float width) => width > 0 ? width : 1f / _state.Matrix.MinScale;

    /// <summary>Corner radii that fit: scaled down together where two of them would
    /// overlap along a side, as CSS and Skia's SetRectRadii do.</summary>
    private static CornerRadius Clamp(CornerRadius radius, Rectangle rect)
    {
        float tl = MathF.Max(0, radius.TopLeft), tr = MathF.Max(0, radius.TopRight);
        float br = MathF.Max(0, radius.BottomRight), bl = MathF.Max(0, radius.BottomLeft);

        float factor = 1f;

        if (tl + tr > 0) factor = MathF.Min(factor, rect.Width / (tl + tr));
        if (bl + br > 0) factor = MathF.Min(factor, rect.Width / (bl + br));
        if (tl + bl > 0) factor = MathF.Min(factor, rect.Height / (tl + bl));
        if (tr + br > 0) factor = MathF.Min(factor, rect.Height / (tr + br));

        factor = MathF.Max(0, factor);

        return factor < 1f ? new CornerRadius(tl * factor, tr * factor, br * factor, bl * factor) : new CornerRadius(tl, tr, br, bl);
    }

    private static Rectangle Inflate(Rectangle rect, float by) =>
        new(rect.X - by, rect.Y - by, rect.Width + by * 2, rect.Height + by * 2);

    private static Rectangle Bounds(Point a, Point b, Point c, Point d)
    {
        float left = MathF.Min(MathF.Min(a.X, b.X), MathF.Min(c.X, d.X));
        float top = MathF.Min(MathF.Min(a.Y, b.Y), MathF.Min(c.Y, d.Y));
        float right = MathF.Max(MathF.Max(a.X, b.X), MathF.Max(c.X, d.X));
        float bottom = MathF.Max(MathF.Max(a.Y, b.Y), MathF.Max(c.Y, d.Y));

        return new Rectangle(left, top, right - left, bottom - top);
    }

    private static Vertex DeviceVertex(float x, float y, uint color, float kind, float u, float v, float e2 = 0) => new()
    {
        X = x,
        Y = y,
        Color = color,
        Kind = kind,
        E0 = u,
        E1 = v,
        E2 = e2,
    };

    private static uint Premultiply(Color color)
    {
        int a = color.A;

        return Pack(
            (byte)((color.R * a + 127) / 255),
            (byte)((color.G * a + 127) / 255),
            (byte)((color.B * a + 127) / 255),
            color.A);
    }

    /// <summary>RGBA in memory order: R the lowest byte, as the shader reads four bytes.</summary>
    private static uint Pack(byte r, byte g, byte b, byte a) =>
        r | ((uint)g << 8) | ((uint)b << 16) | ((uint)a << 24);

    #endregion
}