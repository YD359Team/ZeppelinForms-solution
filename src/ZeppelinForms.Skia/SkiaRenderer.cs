using SkiaSharp;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Effects;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;
using ZeppelinForms.Theming;

namespace ZeppelinForms.Skia;

public static class SkiaRenderer
{
    /// <param name="clearBackground">Clear the whole canvas before drawing.
    /// false is needed where several forms share one surface: a dialog has no right
    /// to erase what the owner window drew under it.</param>
    /// <param name="origin">The form's offset on the surface in logical units.
    /// Zero by default — the form takes the whole surface.</param>
    public static void Render(
        Form form,
        SKCanvas canvas,
        float scale = 1f,
        Rectangle? clip = null,
        bool clearBackground = true,
        Point origin = default)
    {
        // the single point all platforms draw through — this is where the deferred
        // layout is completed. Drawing with stale geometry is not allowed, and there
        // is no point for every backend to know about the deferral
        form.EnsureLayout();

        // back to this count in finally rather than a Restore at the end: a GL
        // surface keeps its canvas between frames, and a frame broken off by an
        // exception left its Save behind — every next frame drew with that frame's
        // scale and clip
        int saveCount = canvas.Save();

        try
        {
            canvas.Scale(scale, scale);
            canvas.Translate(origin.X, origin.Y);

            if (clip is { } dirty)
                canvas.ClipRect(new SKRect(dirty.X, dirty.Y, dirty.X + dirty.Width, dirty.Y + dirty.Height));

            var formRect = new SKRect(0, 0, form.ClientSize.Width, form.ClientSize.Height);

            // the form's background — the theme's unless it has one of its own. It was a
            // fixed white: panels draw no background, and a dark theme showed white
            // between the cards of every page
            Color back = form.EffectiveBackground;
            var backColor = new SKColor(back.R, back.G, back.B, back.A);

            if (clearBackground)
            {
                canvas.Clear(backColor);   // Clear respects the clip
            }
            else
            {
                // the form doesn't draw beyond its area and must not show through:
                // the canvas belongs to someone else, clearing it is not allowed,
                // so we paint exactly what we occupy
                canvas.ClipRect(formRect);

                using var background = new SKPaint { Color = backColor };
                canvas.DrawRect(formRect, background);
            }

            var (rippleActive, rippleOrigin, rippleRadius, rippleColor) = form.ThemeRipple;

            if (rippleActive)
            {
                // the old background stays outside the circle, the new one inside;
                // the content is drawn on top already with the new theme
                canvas.Save();

                using var pathBuilder = new SKPathBuilder();
                pathBuilder.AddCircle(rippleOrigin.X, rippleOrigin.Y, rippleRadius);

                using SKPath path = pathBuilder.Detach();
                canvas.ClipPath(path, antialias: true);

                using var paint = new SKPaint
                {
                    Color = new SKColor(rippleColor.R, rippleColor.G, rippleColor.B, rippleColor.A),
                };

                canvas.DrawRect(new SKRect(0, 0, form.ClientSize.Width, form.ClientSize.Height), paint);
                canvas.Restore();
            }

            var g = new SkiaGraphics(canvas);

            if (form.Content is not null)
                ElementTreeRenderer.Draw(form.Content, g, clip);

            foreach (var overlay in form.Overlays)
                ElementTreeRenderer.Draw(overlay, g, clip);

            if (form.IsInspectorEnabled)
                DrawInspector(form, g);
        }
        finally
        {
            canvas.RestoreToCount(saveCount);
        }
    }

    private static void DrawInspector(Form form, Graphics g)
    {
        UIElement? target = form.InspectedElement;
        if (target is null) return;

        ThemeColors colors = App.Theme.Colors;

        Point absolute = target.GetAbsolutePosition();
        var bounds = new Rectangle(absolute, target.ActualSize);

        // the box model as browsers' tools show it: the margin outside the element,
        // the padding inside its border, the content left clear — the spacing that
        // puts an element where it is shows at a glance
        Thickness margin = target.Margin;
        Thickness padding = target.Padding;

        var outer = new Rectangle(
            new Point(bounds.X - margin.Left, bounds.Y - margin.Top),
            new Size(bounds.Width + margin.Horizontal, bounds.Height + margin.Vertical));

        Color marginTint = new(70, 0xF5, 0x9E, 0x0B);
        Color paddingTint = new(70, 0x22, 0xC5, 0x5E);
        Color contentTint = colors.Accent.WithA(40);

        Band(g, outer, bounds, marginTint);

        var content = new Rectangle(
            new Point(bounds.X + padding.Left, bounds.Y + padding.Top),
            new Size(Math.Max(0, bounds.Width - padding.Horizontal), Math.Max(0, bounds.Height - padding.Vertical)));

        Band(g, bounds, content, paddingTint);
        g.FillRectangle(content, contentTint);
        g.DrawRectangle(bounds, colors.Accent, 1.5f);

        // a chip with the type, the name and the size, in the theme's accent
        string name = string.IsNullOrEmpty(target.Name) ? string.Empty : $" #{target.Name}";
        string info = $"{target.GetType().Name}{name}  {bounds.Width:0} × {bounds.Height:0}";

        Font font = Font.Default.WithSize(Math.Max(10f, Font.Default.Size - 2f));
        Size text = TextMeasurer.Current.MeasureText(info, font);

        var chip = new Rectangle(
            new Point(bounds.X, bounds.Y - text.Height - 10f >= 0 ? bounds.Y - text.Height - 10f : bounds.Bottom + 4f),
            new Size(text.Width + 14f, text.Height + 6f));

        g.FillRoundRectangle(chip, new CornerRadius(4f), colors.Accent);
        g.DrawText(info, chip, colors.TextOnAccent, font, HorizontalContentAlignment.Center, VerticalContentAlignment.Center);
    }

    /// <summary>Fill the band between two rectangles, the inner inside the outer.</summary>
    private static void Band(Graphics g, Rectangle outer, Rectangle inner, Color color)
    {
        if (outer == inner) return;

        g.FillRectangle(new Rectangle(outer.Position, new Size(outer.Width, Math.Max(0, inner.Y - outer.Y))), color);
        g.FillRectangle(new Rectangle(new Point(outer.X, inner.Bottom), new Size(outer.Width, Math.Max(0, outer.Bottom - inner.Bottom))), color);
        g.FillRectangle(new Rectangle(new Point(outer.X, inner.Y), new Size(Math.Max(0, inner.X - outer.X), inner.Height)), color);
        g.FillRectangle(new Rectangle(new Point(inner.Right, inner.Y), new Size(Math.Max(0, outer.Right - inner.Right), inner.Height)), color);
    }

    public static void DrawElement(UIElement element, Graphics g) => ElementTreeRenderer.Draw(element, g);
}