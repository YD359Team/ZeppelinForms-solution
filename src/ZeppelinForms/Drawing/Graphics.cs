using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Core.Text;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Drawing;

public abstract class Graphics
{
    /// <summary>Decorations and an outline for the text drawn now — DrawText and
    /// DrawRuns apply them. The tree renderer sets them for each element from its
    /// text properties and puts the outer ones back after it, so a control draws
    /// its captions as usual and they come out decorated.</summary>
    public TextEffects TextEffects { get; set; }

    /// <summary>The direction of the paragraphs of the text drawn now: the element's
    /// flow direction, set by the tree renderer as it sets <see cref="TextEffects"/>.
    /// A backend that orders text by the Unicode Bidirectional Algorithm takes it as
    /// the paragraph level; Hebrew and Arabic run right to left in either.</summary>
    public FlowDirection TextDirection { get; set; }

    public abstract void DrawImage(
        Rectangle rect, Image image,
        ImageFlip flip = ImageFlip.None,
        ImageLayout layout = ImageLayout.Stretch);

    public abstract void DrawRectangle(Rectangle rect, Color color, float width);
    public abstract void FillRectangle(Rectangle rect, Color color);

    public abstract void DrawText(string text, Point position, Color color, Font font);

    /// <summary>A line of text in a rectangle, placed by the font's measures: its line
    /// box — the ascent over the descent — goes to the top, the middle or the bottom,
    /// so every text of one font in one rectangle stands on one baseline.</summary>
    /// <remarks>
    /// Until 0.15 the glyphs actually drawn were centered, their ink: "OK" and "Apply"
    /// in two equal buttons stood on different baselines, the descenders of the second
    /// pushing its letters up. What doesn't fit across is cut off at the rectangle.
    /// </remarks>
    public abstract void DrawText(
        string text, Rectangle rect, Color color, Font font,
        HorizontalContentAlignment hAlign = HorizontalContentAlignment.Center,
        VerticalContentAlignment vAlign = VerticalContentAlignment.Center);

    /// <summary>A symbol — an arrow, a day's number in its circle, a percentage in its
    /// ring — centered by its own ink rather than by the font's line: it stands alone
    /// in its shape, with no neighbour to share a baseline with, and must look centered
    /// in it.</summary>
    /// <remarks>A backend without glyph bounds places it as text.</remarks>
    public virtual void DrawSymbol(
        string text, Rectangle rect, Color color, Font font,
        HorizontalContentAlignment hAlign = HorizontalContentAlignment.Center,
        VerticalContentAlignment vAlign = VerticalContentAlignment.Center) =>
        DrawText(text, rect, color, font, hAlign, vAlign);


    /// <summary>A line of text placed by the font's metrics rather than by the ink
    /// of its glyphs: where the text is edited or changes, it stays on one baseline.</summary>
    /// <remarks>
    /// <para>
    /// <see cref="DrawText(string, Rectangle, Color, Font, HorizontalContentAlignment, VerticalContentAlignment)"/>
    /// used to center the glyphs actually drawn, which suits a caption that never changes.
    /// In a field that is typed into it moved the line at every letter: "a" sits
    /// lower than "Ag", and the text jumped as it was typed. Here the line's place
    /// comes from the font alone — the ascent and descent every text of it shares,
    /// as <see cref="DrawRuns"/> lays its runs out. What doesn't fit across is cut
    /// off at the rectangle, as DrawText cuts it.
    /// </para>
    /// <para>
    /// Since 0.15 DrawText places by the font too, and the two put a line on the same
    /// baseline; this one stays for the clipping across only, which keeps the
    /// descenders of a line taller than its rectangle.
    /// </para>
    /// </remarks>
    public virtual void DrawTextLine(
        string text, Rectangle rect, Color color, Font font,
        HorizontalContentAlignment hAlign = HorizontalContentAlignment.Left,
        VerticalContentAlignment vAlign = VerticalContentAlignment.Center)
    {
        if (string.IsNullOrEmpty(text)) return;

        // across only: a line taller than the rectangle keeps its descenders
        bool clip = rect.Width < float.MaxValue / 2f;

        if (clip)
        {
            Save();
            ClipRect(new Rectangle(new Point(rect.X, rect.Y - rect.Height), new Size(rect.Width, rect.Height * 3f)));
        }

        DrawRuns([new TextRun(text)], rect, font, color, hAlign, vAlign);

        if (clip) Restore();
    }

    public abstract void FillEllipse(Rectangle rect, Color color);
    public abstract void DrawEllipse(Rectangle rect, Color color, float width);
    public abstract void DrawLine(Point from, Point to, Color color, float width);
    public abstract void DrawPolyline(ReadOnlySpan<Point> points, Color color, float width);
    public abstract void DrawArc(Rectangle rect, float startAngle, float sweepAngle, Color color, float width);
    public abstract void DrawSvgPath(string pathData, Rectangle rect, Color color, float strokeWidth = 0f);
    public abstract void DrawShadow(Rectangle rect, BoxShadow shadow);
    public abstract void FillRoundRectangle(Rectangle rect, CornerRadius radius, Color color);
    public abstract void DrawRoundRectangle(Rectangle rect, CornerRadius radius, Color color, float width);
    /// <summary>The rectangle with its edges moved to the nearest device pixels, under
    /// the current transform: a frame drawn inside it is as crisp on one side as on the
    /// other, wherever the layout put the element.</summary>
    /// <remarks>
    /// Text and padding make positions fractional — a row 16.8 px high puts every
    /// row below it between pixels — and a 1 px line between two pixels is drawn as
    /// two pale ones: one side of a frame looked thinner than the other. Under a
    /// rotation or a skew there are no pixel edges to snap to, and the rectangle is
    /// returned as it is; so it is by a graphics without pixels.
    /// </remarks>
    public virtual Rectangle AlignToPixels(Rectangle rect) => rect;
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