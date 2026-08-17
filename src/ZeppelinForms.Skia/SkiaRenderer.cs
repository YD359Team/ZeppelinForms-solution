using SkiaSharp;
using ZeppelinForms.Forms;

namespace ZeppelinForms.Skia;

public static class SkiaRenderer
{
    public static void Render(Form form, SKCanvas canvas)
    {
        canvas.Clear(SKColors.White);

        // TODO: полноценный обход дерева контролов —
        // пока у Control нет ни детей, ни фона, ни виртуального OnRender.
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
}