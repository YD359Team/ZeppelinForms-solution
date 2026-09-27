using SkiaSharp;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Effects;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;

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

        canvas.Save();
        canvas.Scale(scale, scale);
        canvas.Translate(origin.X, origin.Y);

        if (clip is { } dirty)
            canvas.ClipRect(new SKRect(dirty.X, dirty.Y, dirty.X + dirty.Width, dirty.Y + dirty.Height));

        var formRect = new SKRect(0, 0, form.ClientSize.Width, form.ClientSize.Height);

        if (clearBackground)
        {
            canvas.Clear(SKColors.White);   // Clear respects the clip
        }
        else
        {
            // the form doesn't draw beyond its area and must not show through:
            // the canvas belongs to someone else, clearing it is not allowed,
            // so we paint exactly what we occupy
            canvas.ClipRect(formRect);

            using var background = new SKPaint { Color = SKColors.White };
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

        canvas.Restore();
    }

    private static void DrawInspector(Form form, Graphics g)
    {
        UIElement? target = form.InspectedElement;
        if (target is null) return;

        Point absolute = target.GetAbsolutePosition();
        var bounds = new Rectangle(absolute, target.ActualSize);

        // a translucent highlight + a frame on top of the element
        g.FillRectangle(bounds, new Color(60, 80, 160, 255));
        g.DrawRectangle(bounds, new Color(255, 30, 90, 220), 2f);

        string info = $"{target.GetType().Name} \"{target.Name}\"  " +
                      $"X={absolute.X:0} Y={absolute.Y:0}  " +
                      $"W={target.ActualSize.Width:0} H={target.ActualSize.Height:0}";

        var labelRect = new Rectangle(
            new Point(bounds.X, Math.Max(0, bounds.Y - 20)),
            new Size(Math.Max(bounds.Width, 260), 18));

        g.FillRectangle(labelRect, new Color(230, 20, 20, 20));
        g.DrawText(info, labelRect, Colors.White, Font.Default, HorizontalContentAlignment.Left, VerticalContentAlignment.Center);
    }

    public static void DrawElement(UIElement element, Graphics g) => ElementTreeRenderer.Draw(element, g);
}