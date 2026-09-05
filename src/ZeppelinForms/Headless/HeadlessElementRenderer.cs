using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Headless;

/// <summary>Отрисовки нет — отдаёт пустое изображение нужного размера.</summary>
public sealed class HeadlessElementRenderer : IElementRenderer
{
    public static void Register() => ElementRenderer.Current = new HeadlessElementRenderer();

    public Image Render(UIElement element, int width, int height) =>
        new(Math.Max(1, width), Math.Max(1, height), new byte[Math.Max(1, width) * Math.Max(1, height) * 4]);

}

public class HeadlessGraphics : Graphics
{
    public override void Skew(float sx, float sy) { }
    public override void SaveBlurLayer(float radius) => Save();
    public override void BlurBackdrop(Rectangle bounds, float radius) { }
    public override void FillNoise(Rectangle bounds, float opacity) { }
    public override void DrawReflection(Rectangle bounds, float heightRatio, float gap, float startOpacity) { }
    public override void FillGradient(Rectangle bounds, CornerRadius radius, GradientStop[] stops, float angle) { }

    public override void DrawImage(Rectangle rect, Image image, ImageFlip flip = ImageFlip.None, ImageLayout layout = ImageLayout.Stretch) { }

    public override void DrawRectangle(Rectangle rect, Color color, float width)
    {
    }

    public override void FillRectangle(Rectangle rect, Color color)
    {
    }

    public override void DrawText(string text, Point position, Color color, Font font)
    {
    }

    public override void DrawText(string text, Rectangle rect, Color color, Font font, HorizontalContentAlignment hAlign = HorizontalContentAlignment.Center, VerticalContentAlignment vAlign = VerticalContentAlignment.Center)
    {
    }

    public override void FillEllipse(Rectangle rect, Color color)
    {
    }

    public override void DrawEllipse(Rectangle rect, Color color, float width)
    {
    }

    public override void DrawLine(Point from, Point to, Color color, float width)
    {
    }

    public override void DrawPolyline(ReadOnlySpan<Point> points, Color color, float width)
    {
    }

    public override void DrawArc(Rectangle rect, float startAngle, float sweepAngle, Color color, float width)
    {
    }

    public override void DrawSvgPath(string pathData, Rectangle rect, Color color, float strokeWidth = 0)
    {
    }

    public override void DrawShadow(Rectangle rect, BoxShadow shadow)
    {
    }

    public override void FillRoundRectangle(Rectangle rect, CornerRadius radius, Color color)
    {
    }

    public override void DrawRoundRectangle(Rectangle rect, CornerRadius radius, Color color, float width)
    {
    }

    public override void FillPie(Rectangle rect, float startAngle, float sweepAngle, Color color)
    {
    }

    public override void DrawRuns(IReadOnlyList<TextRun> runs, Rectangle rect, Font baseFont, Color baseColor, HorizontalContentAlignment hAlign = HorizontalContentAlignment.Center, VerticalContentAlignment vAlign = VerticalContentAlignment.Center)
    {
    }

    public override void SaveDisabledLayer(float opacity, float desaturation)
    {
    }

    public override void ClipCircle(Point center, float radius)
    {
    }

    public override void ClipRoundRect(Rectangle rect, CornerRadius radius)
    {
    }

    public override void Rotate(float degrees)
    {
    }

    public override void Save()
    {
    }

    public override void ClipRect(Rectangle bounds)
    {
    }

    public override void Restore()
    {
    }

    public override void Translate(float dx, float dy)
    {
    }

    public override void Scale(float sx, float sy)
    {
    }

    public override void SaveLayer(float opacity)
    {
    }
}
