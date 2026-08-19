using SkiaSharp;
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
    }

    private static void Draw(UIElement element, Graphics g)
    {
        g.Save();
        g.Translate(element.Position.X, element.Position.Y);

        switch (element)
        {
            case UnitControl unit:
                unit.Draw(g);
                break;

            case SingleControl single:
                single.Draw(g);
                if (single.Child is not null)
                    Draw(single.Child, g);
                break;

            case PanelControl panel:
                panel.Draw(g);
                foreach (var child in panel.Children)
                    Draw(child, g);
                break;
        }

        g.Restore();
    }
}
