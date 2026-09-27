using ZeppelinForms.Animation;
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
    private static void Draw(UIElement element, Graphics g, Point origin, Rectangle? clip, bool cull)
    {
        // a transition's curves may briefly take the opacity beyond [0; 1] —
        // it is brought into range here, not in the setter
        float opacity = Math.Clamp(element.Opacity, 0f, 1f);

        if (!element.IsVisible || opacity <= 0f) return;
        if (!float.IsFinite(element.ActualSize.Width) || !float.IsFinite(element.ActualSize.Height)) return;

        var placed = new Point(origin.X + element.Position.X, origin.Y + element.Position.Y);

        // the element is entirely outside the visible area — skip it together
        // with its descendants. The draw-time offset is already accounted for
        // in LocalDirtyBounds, so the place from layout is taken here, without it
        if (cull && clip is { } visible &&
            !element.LocalDirtyBounds.Offset(placed.X, placed.Y).IntersectsWith(visible))
            return;

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

        if (!element.IsEnabled)
            g.SaveDisabledLayer(element.DisabledOpacity * opacity, element.DisabledDesaturation);
        else if (opacity < 1f)
            g.SaveLayer(opacity);

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

        if (element.BoxShadow is { } shadow)
            g.DrawShadow(element.LocalBounds, shadow);

        EffectChain? effects = element.EffectsOrNull;

        if (effects is { IsEmpty: false })
            effects.Begin(g, element.LocalBounds);

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
                    g.ClipRect(wrap.ContentBounds);
                    wrap.ApplyChildTransform(g);

                    // a content transform of one's own is the same as rotation:
                    // ZoomBox scales the child, and its absolute coordinates
                    // no longer add up from offsets
                    bool cullChild = cullChildren && !wrap.TransformsChild;

                    Draw(
                        wrap.Child,
                        g,
                        position,
                        cullChild ? Narrow(clip, wrap.ContentBounds, position) : null,
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

        if (effects is { IsEmpty: false })
            effects.End(g, element.LocalBounds);

        if (needsLayer)
            g.Restore();

        g.Restore();
    }

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

        Draw(element, g, origin, clip: null, cull: false);

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