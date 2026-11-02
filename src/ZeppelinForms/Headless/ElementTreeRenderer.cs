using ZeppelinForms.Animation;
using ZeppelinForms.Core.Text;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Effects;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Headless;

/// <summary>
/// Walks the element tree and calls their drawing. Knows nothing about the backend:
/// works through the abstract <see cref="Graphics"/>, so it fits Skia,
/// the headless stub and any future renderer.
/// </summary>
public static class ElementTreeRenderer
{
    /// <param name="clip">The dirty area in absolute coordinates.
    /// null — draw everything.</param>
    public static void Draw(UIElement element, Graphics g, Rectangle? clip = null)
    {
        // inside this scope reading properties returns the intermediate values
        // of running transitions. Outside — the targets: layout, logic and
        // bindings must see what was assigned, not halfway to it
        using (UIElement.BeginPresentation())
            Draw(element, g, Point.Empty, clip, cull: true);
    }

    /// <param name="origin">The parent's absolute position: the walk accumulates
    /// it on the way down instead of walking up to the root for every element.</param>
    /// <param name="clip">The visible area in absolute coordinates: the dirty
    /// rectangle narrowed by the areas of all ancestors. null — no restrictions,
    /// draw everything.</param>
    /// <param name="cull">Whether adding up offsets can be trusted. Under rotation
    /// or a content transform of one's own it can't: rectangles in absolute
    /// coordinates no longer describe the position on the canvas,
    /// and the subtree is drawn entirely.</param>
    /// <remarks>
    /// The clip is narrowed on the way down, not only taken from the dirty area.
    /// Previously culling worked only where the platform gave partial redraws:
    /// on Android and in the browser clip is always null, and a panel with
    /// a hundred rows drew all of them although ten are visible. ClipRect cut
    /// the pixels, but text blobs, paths and shadows still had to be built
    /// for each one.
    /// </remarks>
    /// <param name="disappearing">The element is drawn as it disappears: hidden
    /// already, it is still shown until its exit transition ends.</param>
    private static void Draw(UIElement element, Graphics g, Point origin, Rectangle? clip, bool cull, bool disappearing = false)
    {
        // a transition's curves may briefly take the opacity beyond [0; 1] —
        // it is brought into range here, not in the setter
        float opacity = Math.Clamp(element.Opacity, 0f, 1f);

        if ((!element.IsVisible && !disappearing) || opacity <= 0f) return;
        if (!float.IsFinite(element.ActualSize.Width) || !float.IsFinite(element.ActualSize.Height)) return;

        var placed = new Point(origin.X + element.Position.X, origin.Y + element.Position.Y);

        // the element is entirely outside the visible area — skip it together
        // with its descendants. The draw-time offset is already accounted for
        // in LocalDirtyBounds, so the place from layout is taken here, without it
        if (cull && clip is { } visible &&
            !element.LocalDirtyBounds.Offset(placed.X, placed.Y).IntersectsWith(visible))
            return;

        // an element in a transition moves every frame. Where it is drawn now is
        // reported back, so that the next frame repaints this place after the element
        // has moved on. Only the renderer knows the exact place — with the ancestors'
        // scroll and the transition's current offset — and the frame a transition
        // starts in is drawn before any tick could record anything
        if (element.IsInTransition)
            element.NotePresentedBounds(element.LocalDirtyBounds.Offset(placed.X, placed.Y));

        // but the children get an already shifted origin: the offset is the same
        // translation as Position, and it doesn't get in the way of culling
        var position = new Point(
            placed.X + element.TranslateX,
            placed.Y + element.TranslateY);

        g.Save();
        g.Translate(element.Position.X, element.Position.Y);

        // offset, rotation and scale — all of it, around the element's center.
        // Its inverse is UIElement.TransformPointToLocal, which HitTester uses
        element.ApplyTransform(g);

        // Dimming and opacity — one layer per element.
        // SaveDisabledLayer already handles alpha, so for a disabled
        // element a second layer is not needed.
        bool needsLayer = !element.IsEnabled || opacity < 1f;

        // a blend mode is a layer too, put over what is under it in the mode. An
        // enabled element's opacity rides on the same layer; a disabled one's dimming
        // is a layer of its own inside it
        BlendMode blend = element.BlendMode;
        bool blendLayer = blend != BlendMode.Normal;

        if (blendLayer)
            g.SaveLayer(element.IsEnabled ? opacity : 1f, blend);

        if (!element.IsEnabled)
            g.SaveDisabledLayer(element.DisabledOpacity * opacity, element.DisabledDesaturation);
        else if (opacity < 1f && !blendLayer)
            g.SaveLayer(opacity);

        // the enabled element's opacity went into the blend layer: no second one
        if (blendLayer && element.IsEnabled) needsLayer = false;

        // rotation and scale break the adding up of offsets: under them
        // a rectangle in absolute coordinates no longer describes where the child
        // ends up on the canvas. Culling below is turned off — the whole subtree
        // is drawn. The offset is not in this list: it is already added to position above
        bool cullChildren = cull && !element.HasComplexTransform;

        // There used to be a second rotation here, around the same center, on top
        // of the one ApplyTransform had already applied: an element was drawn at
        // twice its angle. HitTester rotated twice as well, so the picture and the
        // clicks matched each other and nothing looked off in isolation — only
        // GripBox's handles and the dirty bounds, which rotate once, drifted apart
        // from the element. Both second rotations are gone together

        // the halo behind everything of the element, its shadow included: the light is
        // what the element stands in
        if (element.Halo is { } halo)
            DrawHalo(element, halo, g, position);

        if (element.BoxShadow is { } shadow)
            g.DrawShadow(element.LocalBounds, shadow);

        EffectChain? effects = element.EffectsOrNull;

        if (effects is { IsEmpty: false })
            effects.Begin(g, element.LocalBounds);

        // the element's text decorations and outline for every caption it draws;
        // the parent's come back after it, for what the parent draws over its children
        TextEffects outerText = g.TextEffects;
        g.TextEffects = element.CurrentTextEffects;

        // and the direction its text is ordered in
        FlowDirection outerDirection = g.TextDirection;
        g.TextDirection = element.EffectiveFlowDirection;

        DrawBody(element, g, position, clip, cullChildren);

        // the light along the border over what the element drew: a neon tube lies on
        // the border, and its light falls on the content near it too
        if (element.Glow is { } glow)
            DrawGlow(element, glow, g);

        g.TextEffects = outerText;
        g.TextDirection = outerDirection;

        if (effects is { IsEmpty: false })
            effects.End(g, element.LocalBounds);

        if (needsLayer)
            g.Restore();

        if (blendLayer)
            g.Restore();

        g.Restore();
    }

    /// <summary>What the element draws and its descendants: the body without its shadow,
    /// effects and light — drawn once more, in the halo's color, for a halo of its shape.</summary>
    /// <param name="position">The element's absolute position, its draw-time offset in.</param>
    /// <param name="clip">The visible area, as <see cref="Draw(UIElement, Graphics, Point, Rectangle?, bool, bool)"/> has it.</param>
    /// <param name="cullChildren">Whether the descendants may be culled by it.</param>
    private static void DrawBody(UIElement element, Graphics g, Point position, Rectangle? clip, bool cullChildren)
    {
        switch (element)
        {
            case UnitControl unit:
                unit.Draw(g);
                break;

            case WrapControl wrap:
                wrap.Draw(g);

                if (wrap.Child is not null)
                {
                    g.Save();
                    g.ClipRect(wrap.ClipBounds);
                    wrap.ApplyChildTransform(g);

                    // a content transform of one's own is the same as rotation:
                    // ZoomBox scales the child, and its absolute coordinates
                    // no longer add up from offsets
                    bool cullChild = cullChildren && !wrap.TransformsChild;

                    Draw(
                        wrap.Child,
                        g,
                        position,
                        cullChild ? Narrow(clip, wrap.ClipBounds, position) : null,
                        cullChild);

                    g.Restore();
                }

                // the border must not be clipped by the content
                wrap.DrawOverlay(g);
                break;

            case PanelControl panel:
                panel.Draw(g);
                g.Save();
                g.ClipRect(panel.ClipBounds);

                Rectangle? inside = cullChildren
                    ? Narrow(clip, panel.ClipBounds, position)
                    : null;

                foreach (var child in panel.Children)
                    Draw(child, g, position, inside, cullChildren);

                // outgoing ones on top of the live ones: their place is already taken
                // by neighbours, and under the neighbours the disappearance wouldn't be visible
                if (panel.Exiting is { } exiting)
                    foreach (ExitingChild ghost in exiting)
                        DrawExiting(ghost, g, position);

                g.Restore();

                // the scrollbar must not be clipped by the content
                panel.DrawOverlay(g);
                break;
        }
    }

    // ===== light =====

    /// <summary>A halo: the element's own shape blurred and tinted behind it, or a round
    /// light from its middle out past its edges.</summary>
    private static void DrawHalo(UIElement element, Halo halo, Graphics g, Point position)
    {
        if (halo.Color.A == 0 || halo.Radius <= 0f) return;

        Rectangle bounds = element.LocalBounds;

        if (halo.Shape == HaloShape.Radial)
        {
            var center = new Point(bounds.X + bounds.Width / 2f, bounds.Y + bounds.Height / 2f);

            // from the middle to past the farthest corner: the light is full over the
            // element, and fades out round it
            float inner = MathF.Sqrt(bounds.Width * bounds.Width + bounds.Height * bounds.Height) / 2f;
            float outer = inner + halo.Radius;
            float full = inner / outer;

            GradientStop[] stops =
            [
                new(halo.Color, 0f),
                new(halo.Color.WithA((byte)(halo.Color.A * 0.85f)), full),
                new(halo.Color.WithA((byte)(halo.Color.A * 0.3f)), full + (1f - full) * 0.45f),
                new(halo.Color.WithA(0), 1f),
            ];

            var square = new Rectangle(new Point(center.X - outer, center.Y - outer), new Size(outer * 2f, outer * 2f));

            g.FillRadialGradient(square, new CornerRadius(outer), stops, center, outer);
            return;
        }

        // the body once more, its alpha kept and its colors the halo's, blurred: a
        // glyph glows round its own strokes, a panel round what it holds. Twice — a wide
        // light and a near one: one blur alone spreads a thin stroke too thin to see
        Silhouette(halo.Radius);
        Silhouette(halo.Radius * 0.4f);

        void Silhouette(float blur)
        {
            g.SaveBlurLayer(blur);
            g.SaveLayer(1f);

            DrawBody(element, g, position, clip: null, cullChildren: false);

            g.SaveLayer(1f, BlendMode.SourceIn);
            g.FillRectangle(bounds.Inflate(halo.Reach), halo.Color);
            g.Restore();

            g.Restore();
            g.Restore();
        }
    }

    /// <summary>A glow along the border: standing, or a comet running round it.</summary>
    private static void DrawGlow(UIElement element, Glow glow, Graphics g)
    {
        if (glow.Color.A == 0 || glow.Width <= 0f) return;

        // the line on the border's middle, or inside the bounds for one thinner than it
        float inset = MathF.Max(element.BorderWidth, glow.Width) / 2f;
        Rectangle bounds = element.LocalBounds;

        var line = new Rectangle(
            new Point(bounds.X + inset, bounds.Y + inset),
            new Size(MathF.Max(0f, bounds.Width - inset * 2f), MathF.Max(0f, bounds.Height - inset * 2f)));

        CornerRadius radius = Shrink(element.CornerRadius, inset);

        // a white-hot core in the light's color: what a neon tube looks like
        Color core = Color.Lerp(glow.Color, Colors.White, 0.55f).WithA(glow.Color.A);

        if (!element.GlowRuns)
        {
            // a wide light and a near one: blurred alone, a line of a few pixels spreads
            // too thin to read as light
            g.DrawGlow(line, radius, glow.Color, glow.Width * 3f, glow.Blur);
            g.DrawGlow(line, radius, glow.Color, glow.Width * 2f, glow.Blur * 0.4f);
            g.DrawRoundRectangle(line, radius, glow.Color, glow.Width);
            g.DrawRoundRectangle(line, radius, core, MathF.Max(1f, glow.Width * 0.4f));
            return;
        }

        element.EnsureGlowRunning();

        var path = new BorderPath(line, radius);
        if (path.Length <= 0f) return;

        // the border itself faintly, for the comet to run along
        g.DrawRoundRectangle(line, radius, glow.Color.WithA((byte)(glow.Color.A * 0.18f)), glow.Width);

        float tail = Math.Clamp(glow.CometLength, 0.05f, 1f) * path.Length;
        float head = element.GlowPhase * path.Length;
        int pieces = Math.Clamp((int)(tail / 3f), 12, 96);
        float step = tail / pieces;

        if (glow.Blur > 0f)
        {
            g.SaveBlurLayer(glow.Blur);
            DrawComet(g, path, head, tail, step, pieces, glow.Color, glow.Width * 3f);
            g.Restore();

            g.SaveBlurLayer(glow.Blur * 0.4f);
            DrawComet(g, path, head, tail, step, pieces, glow.Color, glow.Width * 2f);
            g.Restore();
        }

        DrawComet(g, path, head, tail, step, pieces, glow.Color, glow.Width);
        DrawComet(g, path, head, tail * 0.35f, step * 0.35f, pieces, core, MathF.Max(1f, glow.Width * 0.4f));
    }

    /// <summary>The comet as short pieces, brighter and wider towards the head.</summary>
    private static void DrawComet(
        Graphics g, BorderPath path, float head, float tail, float step, int pieces, Color color, float width)
    {
        Point previous = path.At(head - tail);

        for (int i = 1; i <= pieces; i++)
        {
            Point next = path.At(head - tail + step * i);

            // the tail fades in as a square: most of the light is near the head
            float share = (float)i / pieces;
            float strength = share * share;

            g.DrawLine(previous, next, color.WithA((byte)(color.A * strength)), width * (0.45f + 0.55f * share));

            previous = next;
        }
    }

    private static CornerRadius Shrink(CornerRadius radius, float by) => new(
        MathF.Max(0f, radius.TopLeft - by),
        MathF.Max(0f, radius.TopRight - by),
        MathF.Max(0f, radius.BottomRight - by),
        MathF.Max(0f, radius.BottomLeft - by));

    /// <summary>Draw an outgoing child in the look matching the passed part
    /// of the disappearance. No culling: there are only a few ghosts,
    /// and no layout describes their position anymore.</summary>
    private static void DrawExiting(ExitingChild ghost, Graphics g, Point origin)
    {
        UIElement element = ghost.Element;
        VisibilityTransition rule = ghost.Rule;
        float t = ghost.Progress;

        float opacity = 1f + (rule.Opacity - 1f) * t;
        float scale = 1f + (rule.Scale - 1f) * t;

        if (opacity <= 0f) return;

        // scale around the element's center, like ScaleX/ScaleY:
        // disappearing and appearing must be mirror images
        var center = new Point(
            element.Position.X + element.ActualSize.Width / 2f,
            element.Position.Y + element.ActualSize.Height / 2f);

        g.Save();

        g.Translate(center.X + rule.OffsetX * t, center.Y + rule.OffsetY * t);
        g.Scale(scale, scale);
        g.Translate(-center.X, -center.Y);

        if (opacity < 1f) g.SaveLayer(opacity);

        Draw(element, g, origin, clip: null, cull: false, disappearing: true);

        if (opacity < 1f) g.Restore();

        g.Restore();
    }

    /// <summary>Narrow the visible area by the element's content area.
    /// area is given in its own coordinates, position is its absolute position.</summary>
    private static Rectangle? Narrow(Rectangle? clip, Rectangle area, Point position)
    {
        Rectangle absolute = area.Offset(position.X, position.Y);

        return clip is { } visible ? visible.Intersect(absolute) : absolute;
    }
}