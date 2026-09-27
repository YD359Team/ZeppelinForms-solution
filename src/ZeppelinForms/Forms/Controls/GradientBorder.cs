using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// A border with a gradient fill around a single element.
/// Graphics has no gradient stroke, so the ring is assembled from two
/// fills: the gradient over the whole rectangle and the background on top
/// of the inner part. Hence the requirement: <see cref="UIElement.Padding"/>
/// no less than <see cref="DecoratedWrapControl.BorderWidth"/>, otherwise
/// the child will overlap the border.
/// </summary>
public class GradientBorder : DecoratedWrapControl
{
    /// <summary>Gradient stops. Fewer than two — the border is drawn with the plain
    /// <see cref="DecoratedWrapControl.BorderColor"/>, like Border.</summary>
    public List<GradientStop> Stops { get; init; } = [];

    /// <summary>Gradient direction in degrees: 0 — left to right.</summary>
    public float Angle
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            InvalidateVisual();
        }
    }

    private bool HasGradient => Stops.Count >= 2;

    public GradientBorder()
    {
        SetControlDefault(BorderWidthProperty, 1f);
        SetControlDefault(PaddingProperty, 1f);
    }

    public GradientBorder(UIElement child) : this()
    {
        Child = child;
    }

    public GradientBorder SetStops(params GradientStop[] stops)
    {
        Stops.Clear();
        Stops.AddRange(stops);
        InvalidateVisual();

        return this;
    }

    /// <summary>An even transition between two colors.</summary>
    public GradientBorder SetStops(Color from, Color to) =>
        SetStops(new GradientStop(from, 0f), new GradientStop(to, 1f));

    /// <summary>The background is filled by us, inside the ring — otherwise
    /// it would lie over the whole area and paint over the gradient.</summary>
    protected override Color CurrentBackground => Colors.Transparent;

    /// <summary>While there are enough stops for a gradient,
    /// the base's solid border is suppressed.</summary>
    protected override Color CurrentBorderColor =>
        HasGradient ? Colors.Transparent : base.CurrentBorderColor;

    protected override void DrawContent(Graphics g)
    {
        Rectangle bounds = LocalBounds;

        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        if (HasGradient)
            g.FillGradient(bounds, CornerRadius, [.. Stops], Angle);

        if (Background.A == 0) return;

        Rectangle inner = HasGradient ? bounds.Inflate(-BorderWidth) : bounds;

        if (inner.Width <= 0 || inner.Height <= 0) return;

        g.FillRoundRectangle(inner, Deflate(CornerRadius, HasGradient ? BorderWidth : 0f), Background);
    }

    private static CornerRadius Deflate(CornerRadius radius, float amount)
    {
        if (amount <= 0f || radius.IsZero) return radius;

        return new CornerRadius(
            Math.Max(0f, radius.TopLeft - amount),
            Math.Max(0f, radius.TopRight - amount),
            Math.Max(0f, radius.BottomRight - amount),
            Math.Max(0f, radius.BottomLeft - amount));
    }
}