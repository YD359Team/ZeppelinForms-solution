using SkiaSharp;
using ZeppelinForms.Drawing;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;

namespace ZeppelinForms.Skia;

public static class SkiaRenderer
{
    public static void Render(Form form, SKCanvas canvas)
    {
        canvas.Clear(SKColors.White);

        var content = form.Content;

        if (content is not null)
        {
            var rect = new SKRect(
                content.Position.X,
                content.Position.Y,
                content.Position.X + content.Size.Width,
                content.Position.Y + content.Size.Height);

            using var paint = new SKPaint
            {
                Color = SKColors.LightGray,
                IsAntialias = true,
            };

            canvas.DrawRect(rect, paint);
        }
    }

    static void Draw(UIElement element, Graphics g)
    {
        switch (element)
        {
            case UnitControl unit:
                unit.Draw(g);
                break;

            case SingleControl content:
                content.Draw(g); // фон/рамка самого контрола
                if (content.Child is not null)
                    Draw(content.Child, g);
                break;

            case PanelControl panel:
                panel.Draw(g);
                foreach (var child in panel.Children)
                    Draw(child, g);
                break;
        }
    }
}