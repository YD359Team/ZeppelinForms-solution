using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Прогресс дугой: полный круг или любая его часть — полукруг, сектор.
/// </summary>
/// <remarks>
/// Полукруглый вариант — это не отдельный контрол, а тот же самый
/// с другой дугой: StartAngle 180 и SweepAngle 180. Раскладка при этом
/// меняется: полукруг занимает вдвое меньше высоты, чем ширины,
/// и центр круга уезжает к нижнему краю.
/// </remarks>
public partial class CircularProgressBar : DecoratedControl
{
    private float _value;

    public float Minimum
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            InvalidateVisual();
        }
    } = 0f;

    public float Maximum
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            InvalidateVisual();
        }
    } = 100f;

    public float Value
    {
        get => _value;
        set
        {
            float clamped = Math.Clamp(value, Minimum, Maximum);
            if (Math.Abs(_value - clamped) < 0.001f) return;

            _value = clamped;
            InvalidateVisual();
        }
    }

    public float ArcThickness
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // толщина дуги входит в размер круга, а не только в отрисовку
            Invalidate();
        }
    } = 8f;

    public bool ShowPercentage
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            InvalidateVisual();
        }
    } = true;

    /// <summary>Откуда начинать дугу: −90 — с 12 часов, 180 — с 9 часов.</summary>
    public float StartAngle
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Invalidate();
        }
    } = -90f;

    /// <summary>Какую часть круга занимает шкала. 360 — полный круг,
    /// 180 вместе со StartAngle = 180 — полукруг.</summary>
    public float SweepAngle
    {
        get;
        set
        {
            if (field == value) return;

            field = Math.Clamp(value, 1f, 360f);

            // от развёрнутости шкалы зависит и желаемый размер контрола
            Invalidate();
        }
    } = 360f;

    [Styled(Category = "Progress")]
    public partial Color FillColor { get; set; }
    private static Color FillColorDefault => new(255, 0x0D, 0x6E, 0xFD);

    [Styled(Category = "Progress")]
    public partial Color TrackColor { get; set; }
    private static Color TrackColorDefault => new(255, 230, 230, 230);

    /// <summary>Шкала занимает половину круга или меньше: раскладка тогда
    /// другая — высота вдвое меньше ширины, центр внизу.</summary>
    private bool IsHalf => SweepAngle <= 180f;

    private float Fraction
    {
        get
        {
            float range = Maximum - Minimum;
            return range <= 0 ? 0 : Math.Clamp((_value - Minimum) / range, 0f, 1f);
        }
    }

    protected override void DrawContent(Graphics g)
    {
        var content = this.ContentBounds;

        // круг вписываем в квадрат по меньшей стороне, иначе получится эллипс.
        // У полукруга по высоте нужна только половина, поэтому ей позволено
        // быть вдвое меньше
        float available = IsHalf
            ? Math.Min(content.Width, content.Height * 2f)
            : Math.Min(content.Width, content.Height);

        float diameter = available - ArcThickness;
        if (diameter <= 0) return;

        float top = IsHalf
            ? content.Y + content.Height - diameter / 2f - ArcThickness / 2f
            : content.Y + (content.Height - diameter) / 2f;

        var circle = new Rectangle(
            new Point(content.X + (content.Width - diameter) / 2f, top),
            new Size(diameter, diameter));

        g.DrawArc(circle, StartAngle, SweepAngle, TrackColor, ArcThickness);

        float fraction = Fraction;
        if (fraction > 0)
            g.DrawArc(circle, StartAngle, SweepAngle * fraction, FillColor, ArcThickness);

        if (!ShowPercentage) return;

        // у полукруга подпись садится внутрь дуги, а не в центр контрола:
        // центр круга у него на нижнем краю
        var textArea = IsHalf
            ? new Rectangle(
                new Point(circle.X, circle.Y + diameter / 2f - ArcThickness - LineHeight),
                new Size(diameter, LineHeight))
            : content;

        g.DrawText($"{fraction * 100:0}%", textArea, TextColor, EffectiveFont,
            HorizontalContentAlignment.Center, VerticalContentAlignment.Center);
    }

    private float LineHeight => TextMeasurer.Current.MeasureText("0%", EffectiveFont).Height;

    protected override Size MeasureOverride(Size availableSize)
    {
        var content = IsHalf
            ? new Size(128 + Padding.Horizontal, 72 + Padding.Vertical)
            : new Size(64 + Padding.Horizontal, 64 + Padding.Vertical);

        return ResolveSize(content, availableSize);
    }
}