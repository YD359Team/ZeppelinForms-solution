using SkiaSharp;
using System.Diagnostics;
using ZeppelinForms.Drawing;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Skia;

public static class SkiaRenderer
{
    public static void Render(Form form, SKCanvas canvas)
    {
        canvas.Clear(SKColors.White);

        if (form.Content is not null)
            Draw(form.Content, new SkiaGraphics(canvas));

        // Overlays рисуются вторым, независимым проходом — без клипа
        // предков, поверх основного дерева
        foreach (var overlay in form.Overlays)
            Draw(overlay, new SkiaGraphics(canvas));
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
