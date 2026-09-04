using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Drawing;

public abstract class Graphics
{
    public abstract void DrawImage(
        Rectangle rect, Image image,
        ImageFlip flip = ImageFlip.None,
        ImageLayout layout = ImageLayout.Stretch);

    public abstract void DrawRectangle(Rectangle rect, Color color, float width);
    public abstract void FillRectangle(Rectangle rect, Color color);

    public abstract void DrawText(string text, Point position, Color color, Font font);

    public abstract void DrawText(
        string text, Rectangle rect, Color color, Font font,
        HorizontalContentAlignment hAlign = HorizontalContentAlignment.Center,
        VerticalContentAlignment vAlign = VerticalContentAlignment.Center);

    public abstract void FillEllipse(Rectangle rect, Color color);
    public abstract void DrawEllipse(Rectangle rect, Color color, float width);
    public abstract void DrawLine(Point from, Point to, Color color, float width);
    public abstract void DrawPolyline(ReadOnlySpan<Point> points, Color color, float width);
    public abstract void DrawArc(Rectangle rect, float startAngle, float sweepAngle, Color color, float width);
    public abstract void DrawSvgPath(string pathData, Rectangle rect, Color color, float strokeWidth = 0f);
    public abstract void DrawShadow(Rectangle rect, BoxShadow shadow);
    public abstract void FillRoundRectangle(Rectangle rect, CornerRadius radius, Color color);
    public abstract void DrawRoundRectangle(Rectangle rect, CornerRadius radius, Color color, float width);
    public abstract void FillPie(Rectangle rect, float startAngle, float sweepAngle, Color color);
    public abstract void DrawRuns(
    IReadOnlyList<TextRun> runs, Rectangle rect, Font baseFont, Color baseColor,
    HorizontalContentAlignment hAlign = HorizontalContentAlignment.Center,
    VerticalContentAlignment vAlign = VerticalContentAlignment.Center);
    /// <summary>Слой с приглушением: всё нарисованное внутри теряет
    /// насыщенность и прозрачность.</summary>
    public abstract void SaveDisabledLayer(float opacity, float desaturation);
    /// <summary>Ограничить отрисовку кругом. Нужно для эффекта волны.</summary>
    public abstract void ClipCircle(Point center, float radius);
    public abstract void Skew(float sx, float sy);

    /// <summary>Слой, содержимое которого будет размыто при закрытии.</summary>
    public abstract void SaveBlurLayer(float radius);

    /// <summary>Размыть то, что уже нарисовано под указанной областью.</summary>
    public abstract void BlurBackdrop(Rectangle bounds, float radius);

    /// <summary>Наложить шум — фактура матового стекла.</summary>
    public abstract void FillNoise(Rectangle bounds, float opacity);

    /// <summary>Отражение содержимого области с затуханием вниз.</summary>
    public abstract void DrawReflection(Rectangle bounds, float heightRatio, float gap, float startOpacity);

    /// <summary>Заливка градиентом.</summary>
    public abstract void FillGradient(Rectangle bounds, CornerRadius radius, GradientStop[] stops, float angle);

    public abstract void ClipRoundRect(Rectangle rect, CornerRadius radius);
    public abstract void Rotate(float degrees);
    public abstract void Save();
    public abstract void ClipRect(Rectangle bounds);
    public abstract void Restore();
    public abstract void Translate(float dx, float dy);
    public abstract void Scale(float sx, float sy);

    /// <summary>Начинает слой с прозрачностью: всё нарисованное до Restore() смешается как единое целое.</summary>
    public abstract void SaveLayer(float opacity);

}