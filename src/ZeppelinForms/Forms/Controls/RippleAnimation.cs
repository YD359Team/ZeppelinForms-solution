using ZeppelinForms.Animation;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// A ripple spreading from the press point. Keeps its state separately,
/// so that any control can mix it into its drawing.
/// </summary>
public sealed class RippleAnimation
{
    private readonly UIElement _owner;

    private Point _origin;
    private float _radius;
    private float _alpha;
    private bool _active;

    public RippleAnimation(UIElement owner) => _owner = owner;

    public Color Color { get; set; } = new Color(60, 255, 255, 255);
    public int DurationMs { get; set; } = 420;

    /// <summary>Start the ripple from a point in the control's coordinates.</summary>
    public void Start(Point localOrigin)
    {
        _origin = localOrigin;
        _active = true;

        Size size = _owner.ActualSize;

        // radius to the farthest corner: the ripple must cover the whole control
        float maxRadius = MathF.Sqrt(
            MathF.Max(localOrigin.X, size.Width - localOrigin.X) * MathF.Max(localOrigin.X, size.Width - localOrigin.X) +
            MathF.Max(localOrigin.Y, size.Height - localOrigin.Y) * MathF.Max(localOrigin.Y, size.Height - localOrigin.Y));

        _radius = 0f;
        _alpha = 1f;

        _owner.Animate("ripple", 0f, 1f, TimeSpan.FromMilliseconds(DurationMs),
            Interpolators.Float,
            value =>
            {
                _radius = maxRadius * value;

                // opacity falls off faster than the radius,
                // otherwise the ripple cuts off sharply at the edge
                _alpha = 1f - value * value;

                _owner.InvalidateVisual();
            },
            Easing.EaseOut,
            completed: () =>
            {
                _active = false;
                _owner.InvalidateVisual();
            });
    }

    /// <summary>Draw the ripple on top of the control's content.</summary>
    public void Draw(Graphics g, Rectangle bounds, CornerRadius radius)
    {
        if (!_active || _radius <= 0f || _alpha <= 0f) return;

        var color = new Color((byte)(Color.A * _alpha), Color.R, Color.G, Color.B);

        g.Save();

        // double clipping: by the control's shape and by the ripple's circle
        g.ClipRoundRect(bounds, radius);
        g.ClipCircle(_origin, _radius);

        g.FillRectangle(bounds, color);

        g.Restore();
    }
}