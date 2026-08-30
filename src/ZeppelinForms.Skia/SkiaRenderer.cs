using SkiaSharp;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;

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

        var g = new SkiaGraphics(canvas);

        if (form.Content is not null)
            Draw(form.Content, g, clip);

        foreach (var overlay in form.Overlays)
            Draw(overlay, g, clip);

        if (form.IsInspectorEnabled)
            DrawInspector(form, g);

        canvas.Restore();
    }

    private static void DrawInspector(Form form, Graphics g)
    {
        UIElement? target = form.InspectedElement;
        if (target is null) return;

        Point absolute = target.GetAbsolutePosition();
        var bounds = new Rectangle(absolute, target.Size);

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

        if (element.Rotation != 0f)
        {
            // поворот вокруг центра: сдвиг в центр, поворот, сдвиг обратно
            Point center = element.Center;
            g.Translate(center.X, center.Y);
            g.Rotate(element.Rotation);
            g.Translate(-center.X, -center.Y);
        }

        // слой нужен, только если прозрачность реально задана: SaveLayer —
        // это отдельная offscreen-поверхность, дорого делать её на каждый элемент
        bool needsLayer = element.Opacity < 1f;
        if (needsLayer)
            g.SaveLayer(element.Opacity);

        if (element.BoxShadow is { } shadow)
            g.DrawShadow(element.LocalBounds, shadow);

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
                    Draw(wrap.Child, g);
                    g.Restore();
                }
                break;

            case PanelControl panel:
                panel.Draw(g);
                g.Save();
                g.ClipRect(panel.ContentBounds);
                foreach (var child in panel.Children)
                    Draw(child, g);
                g.Restore();
                break;
        }

        if (needsLayer)
            g.Restore();

        g.Restore();
    }

    public static void DrawElement(UIElement element, Graphics g) => Draw(element, g);
}

public sealed class SkiaElementRenderer : IElementRenderer
{
    public static void Register() => ElementRenderer.Current = new SkiaElementRenderer();

    public Image Render(UIElement element, int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);

        using SKSurface surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("Не удалось создать offscreen-поверхность.");

        surface.Canvas.Clear(SKColors.Transparent);

        var g = new SkiaGraphics(surface.Canvas);

        // Draw() сдвигает канвас на element.Position — для снимка нам нужен
        // элемент в начале координат, поэтому компенсируем сдвиг заранее
        g.Save();
        g.Translate(-element.Position.X, -element.Position.Y);
        SkiaRenderer.DrawElement(element, g);
        g.Restore();

        surface.Canvas.Flush();

        using SKImage snapshot = surface.Snapshot();
        using SKPixmap pixmap = snapshot.PeekPixels();

        byte[] pixels = new byte[width * height * 4];
        Marshal.Copy(pixmap.GetPixels(), pixels, 0, pixels.Length);

        return new Image(width, height, pixels);
    }
}
