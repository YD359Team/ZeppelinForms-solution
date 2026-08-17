using SkiaSharp;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Skia;

public sealed class SkiaGraphics : Graphics
{
    private readonly SKCanvas _canvas;

    public SkiaGraphics(SKCanvas canvas) => _canvas = canvas;

    public override void FillRectangle(Rectangle rect, Color color)
    {
        using var paint = new SKPaint
        {
            Color = new SKColor(color.R, color.G, color.B, color.A),
            IsAntialias = true,
        };

        _canvas.DrawRect(
            new SKRect(rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height),
            paint);
    }

    public override void DrawText(string text, Point position, Color color)
    {
        using var paint = new SKPaint
        {
            Color = new SKColor(color.R, color.G, color.B, color.A),
            IsAntialias = true,
        };

        using var font = new SKFont(SKTypeface.Default, 16);

        _canvas.DrawText(text, position.X, position.Y, font, paint);
    }

    public override void Save() => _canvas.Save();
    public override void Restore() => _canvas.Restore();
    public override void Translate(float dx, float dy) => _canvas.Translate(dx, dy);
}