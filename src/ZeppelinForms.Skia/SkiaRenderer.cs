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
    public static void Render(Form form, SKCanvas canvas, float scale = 1f, Rectangle? clip = null)
    {
        canvas.Save();
        canvas.Scale(scale, scale);

        if (clip is { } dirty)
        {
            canvas.ClipRect(new SKRect(dirty.X, dirty.Y, dirty.X + dirty.Width, dirty.Y + dirty.Height));
            canvas.Clear(SKColors.White);   // Clear уважает клип
        }
        else
        {
            canvas.Clear(SKColors.White);
        }

        var (rippleActive, rippleOrigin, rippleRadius, rippleColor) = form.ThemeRipple;

        if (rippleActive)
        {
            // старый фон остаётся за пределами круга, новый — внутри;
            // содержимое рисуется поверх уже с новой темой
            canvas.Save();

            using var path = new SKPath();
            path.AddCircle(rippleOrigin.X, rippleOrigin.Y, rippleRadius);
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

        // полупрозрачная подсветка + рамка поверх элемента
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

    private static void Draw(UIElement element, Graphics g, Rectangle? clip = null)
    {
        if (!element.IsVisible || element.Opacity <= 0f) return;
        if (!float.IsFinite(element.ActualSize.Width) || !float.IsFinite(element.ActualSize.Height)) return;

        // элемент целиком вне грязной области — пропускаем вместе с потомками
        if (clip is { } dirty && !element.DirtyBounds.IntersectsWith(dirty))
            return;

        g.Save();
        g.Translate(element.Position.X, element.Position.Y);

        // Приглушение и прозрачность — один слой на элемент.
        // SaveDisabledLayer уже умеет альфу, поэтому при выключенном
        // элементе второй слой не нужен.
        bool needsLayer = !element.IsEnabled || element.Opacity < 1f;

        if (!element.IsEnabled)
            g.SaveDisabledLayer(element.DisabledOpacity * element.Opacity, element.DisabledDesaturation);
        else if (element.Opacity < 1f)
            g.SaveLayer(element.Opacity);

        if (element.Rotation != 0f)
        {
            // поворот вокруг центра: сдвиг в центр, поворот, сдвиг обратно
            Point center = element.Center;
            g.Translate(center.X, center.Y);
            g.Rotate(element.Rotation);
            g.Translate(-center.X, -center.Y);
        }

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
                    Draw(wrap.Child, g, clip);
                    g.Restore();
                }

                // рамка не должна обрезаться содержимым
                wrap.DrawOverlay(g);
                break;

            case PanelControl panel:
                panel.Draw(g);
                g.Save();
                g.ClipRect(panel.ContentBounds);
                foreach (var child in panel.Children)
                    Draw(child, g, clip);
                g.Restore();

                // полоса прокрутки не должна обрезаться содержимым
                panel.DrawOverlay(g);
                break;
        }

        if (effects is { IsEmpty: false })
            effects.End(g, element.LocalBounds);

        if (needsLayer)
            g.Restore();

        g.Restore();
    }

    public static void DrawElement(UIElement element, Graphics g) => ElementTreeRenderer.Draw(element, g);
}
