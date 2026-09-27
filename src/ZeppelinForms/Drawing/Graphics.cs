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
    /// <summary>A dimmed layer: everything drawn inside loses saturation
    /// and opacity.</summary>
    public abstract void SaveDisabledLayer(float opacity, float desaturation);
    /// <summary>Restrict drawing to a circle. Needed for the ripple effect.</summary>
    public abstract void ClipCircle(Point center, float radius);
    public abstract void Skew(float sx, float sy);

    /// <summary>A layer whose content will be blurred when it is closed.</summary>
    public abstract void SaveBlurLayer(float radius);

    /// <summary>Blur what is already drawn under the given area.</summary>
    public abstract void BlurBackdrop(Rectangle bounds, float radius);

    /// <summary>Overlay noise — the texture of frosted glass.</summary>
    public abstract void FillNoise(Rectangle bounds, float opacity);

    /// <summary>A reflection of the area's content fading downward.</summary>
    public abstract void DrawReflection(Rectangle bounds, float heightRatio, float gap, float startOpacity);

    /// <summary>A gradient fill.</summary>
    public abstract void FillGradient(Rectangle bounds, CornerRadius radius, GradientStop[] stops, float angle);
    /// <summary>Whether this canvas can divert drawing into a separate layer
    /// and hand over its content. Effects built on redrawing must ask:
    /// without support an element drawn into a capture simply disappears.</summary>
    public virtual bool SupportsLayerCapture => false;

    /// <summary>Divert further drawing into a separate layer the size of the given
    /// area. It won't get onto the canvas.</summary>
    public virtual void BeginCapture(Rectangle bounds) { }

    /// <summary>Close the layer and take its content. Draws nothing:
    /// what to do with the snapshot is up to the caller.</summary>
    public virtual LayerCapture? EndCapture() => null;

    /// <summary>Draw a capture. sourceClip — a part of the capture in its own
    /// coordinates; null means "whole".</summary>
    public virtual void DrawCapture(
        LayerCapture capture,
        Rectangle target,
        Rectangle? sourceClip = null,
        ColorChannels channels = ColorChannels.All,
        CaptureBlend blend = CaptureBlend.Normal,
        float opacity = 1f)
    { }
    /// <summary>Fill a closed polygon. The outline closes by itself:
    /// there is no need to repeat the first point at the end.</summary>
    public abstract void FillPolygon(ReadOnlySpan<Point> points, Color color);

    public abstract void ClipRoundRect(Rectangle rect, CornerRadius radius);
    public abstract void Rotate(float degrees);
    public abstract void Save();
    public abstract void ClipRect(Rectangle bounds);
    public abstract void Restore();
    public abstract void Translate(float dx, float dy);
    public abstract void Scale(float sx, float sy);

    /// <summary>Starts a layer with opacity: everything drawn until Restore() blends as a single whole.</summary>
    public abstract void SaveLayer(float opacity);

}