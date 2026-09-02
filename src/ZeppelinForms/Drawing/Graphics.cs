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