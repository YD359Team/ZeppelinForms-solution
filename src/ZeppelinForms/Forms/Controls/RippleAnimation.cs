using ZeppelinForms.Animation;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Волна, расходящаяся от точки нажатия. Держит своё состояние отдельно,
/// чтобы любой контрол мог подмешать её в отрисовку.
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

    /// <summary>Запустить волну из точки в координатах контрола.</summary>
    public void Start(Point localOrigin)
    {
        _origin = localOrigin;
        _active = true;

        Size size = _owner.ActualSize;

        // радиус до самого дальнего угла: волна должна накрыть контрол целиком
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

                // прозрачность спадает быстрее радиуса, иначе волна
                // резко обрывается на границе
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

    /// <summary>Нарисовать волну поверх содержимого контрола.</summary>
    public void Draw(Graphics g, Rectangle bounds, CornerRadius radius)
    {
        if (!_active || _radius <= 0f || _alpha <= 0f) return;

        var color = new Color((byte)(Color.A * _alpha), Color.R, Color.G, Color.B);

        g.Save();

        // двойное отсечение: по форме контрола и по кругу волны
        g.ClipRoundRect(bounds, radius);
        g.ClipCircle(_origin, _radius);

        g.FillRectangle(bounds, color);

        g.Restore();
    }
}