using SkiaSharp;
using System.Diagnostics;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Skia;

public static class SkiaRenderer
{
    public static void Render(Form form, SKCanvas canvas)
    {
        canvas.Clear(SKColors.White);

        var g = new SkiaGraphics(canvas);

        if (form.Content is not null)
            Draw(form.Content, g);

        foreach (var overlay in form.Overlays)
            Draw(overlay, g);

        if (form.IsInspectorEnabled)
            DrawInspector(form, g);
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
                      $"W={target.Size.Width:0} H={target.Size.Height:0}";

        var labelRect = new Rectangle(
            new Point(bounds.X, Math.Max(0, bounds.Y - 20)),
            new Size(Math.Max(bounds.Width, 260), 18));

        g.FillRectangle(labelRect, new Color(230, 20, 20, 20));
        g.DrawText(info, labelRect, Colors.White, HorizontalAlign.Left, VerticalAlign.Center);
    }

    private static void Draw(UIElement element, Graphics g)
    {
        if (!element.IsVisible)
            return;

        g.Save();
        g.Translate(element.Position.X, element.Position.Y);

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

        g.Restore();
    }
}
