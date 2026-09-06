using ZeppelinForms.Animation;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Индикатор незавершённой операции. Длительность неизвестна —
/// для известной есть ProgressBar.
/// </summary>
public partial class Loader : DecoratedControl
{
    private const string AnimationKey = "loader-phase";

    private float _phase;

    public LoaderStyle Style { get; set; } = LoaderStyle.Ring;

    [Styled(Category = "Appearance")]
    public partial Color Color { get; set; }
    private static Color ColorDefault => new(255, 0x0D, 0x6E, 0xFD);

    [Styled(Category = "Appearance")]
    /// <summary>Цвет дорожки под индикатором. Прозрачный — не рисовать.</summary>
    public partial Color TrackColor { get; set; }
    private static Color TrackColorDefault => Colors.Transparent;

    /// <summary>Толщина линии. Не Thickness: так называется тип отступов,
    /// и внутри класса имя перекрыло бы его.</summary>
    public float StrokeWidth { get; set; } = 3f;

    /// <summary>Желаемый размер индикатора без учёта Padding.</summary>
    public float IndicatorSize { get; set; } = 32f;

    /// <summary>Длительность одного оборота.</summary>
    public TimeSpan Period { get; set; } = TimeSpan.FromMilliseconds(1200);

    /// <summary>Количество лучей у Spinner и точек у Dots.</summary>
    public int ElementCount { get; set; } = 8;

    public bool IsRunning
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            if (value) Start();
            else Stop();
        }
    } = true;

    protected override void OnAttached()
    {
        if (IsRunning && IsVisible) Start();
    }

    // снимать анимацию в OnDetached не нужно: DetachTree выкидывает
    // всё, чей Target в убираемом поддереве

    private void Start()
    {
        this.AnimateLoop(AnimationKey, Period, phase => _phase = phase);
    }

    private void Stop()
    {
        this.StopAnimation(AnimationKey);

        _phase = 0f;
        InvalidateVisual();
    }

    // фон, рамку и скругление рисует база
    protected override void DrawContent(Graphics g)
    {
        Rectangle content = ContentBounds;

        if (content.Width <= 0 || content.Height <= 0) return;

        switch (Style)
        {
            case LoaderStyle.Ring: DrawRing(g, content); break;
            case LoaderStyle.Spinner: DrawSpinner(g, content); break;
            case LoaderStyle.Dots: DrawDots(g, content); break;
            case LoaderStyle.Bar: DrawBar(g, content); break;
        }
    }

    private void DrawRing(Graphics g, Rectangle content)
    {
        float diameter = Math.Min(content.Width, content.Height) - StrokeWidth;
        if (diameter <= 0) return;

        var circle = new Rectangle(
            new Point(
                content.X + (content.Width - diameter) / 2f,
                content.Y + (content.Height - diameter) / 2f),
            new Size(diameter, diameter));

        if (TrackColor.A > 0)
            g.DrawEllipse(circle, TrackColor, StrokeWidth);

        // длина дуги пульсирует от 30° до 270°, а её начало проходит
        // два оборота за период — вместе это даёт «догоняющий хвост»
        float grow = (1f - MathF.Cos(_phase * MathF.Tau)) / 2f;
        float sweep = 30f + 240f * grow;
        float start = _phase * 720f - 90f;

        g.DrawArc(circle, start, sweep, Color, StrokeWidth);
    }

    private void DrawSpinner(Graphics g, Rectangle content)
    {
        int count = Math.Max(3, ElementCount);

        float radius = (Math.Min(content.Width, content.Height) - StrokeWidth) / 2f;
        if (radius <= 0) return;

        var center = new Point(
            content.X + content.Width / 2f,
            content.Y + content.Height / 2f);

        float inner = radius * 0.5f;

        // ведущий луч дискретен: непрерывное вращение здесь читается
        // хуже, чем щелчки, — так же ведёт себя системный индикатор
        int head = (int)(_phase * count) % count;

        for (int i = 0; i < count; i++)
        {
            int behind = (head - i + count) % count;
            float fade = 1f - behind / (float)count;

            float angle = i / (float)count * MathF.Tau - MathF.PI / 2f;
            float cos = MathF.Cos(angle);
            float sin = MathF.Sin(angle);

            g.DrawLine(
                new Point(center.X + cos * inner, center.Y + sin * inner),
                new Point(center.X + cos * radius, center.Y + sin * radius),
                new Color((byte)(Color.A * fade), Color.R, Color.G, Color.B),
                StrokeWidth);
        }
    }

    private void DrawDots(Graphics g, Rectangle content)
    {
        int count = Math.Max(2, ElementCount);

        float step = content.Width / count;
        float maxRadius = Math.Min(step, content.Height) / 2f;
        if (maxRadius <= 0) return;

        float centerY = content.Y + content.Height / 2f;

        for (int i = 0; i < count; i++)
        {
            // каждая точка отстаёт от предыдущей на долю периода
            float phase = _phase - i / (float)count;
            if (phase < 0f) phase += 1f;

            // всплеск в первой половине своего отрезка, покой во второй
            float pulse = phase < 0.5f ? MathF.Sin(phase * MathF.Tau) : 0f;

            float radius = maxRadius * (0.45f + 0.55f * pulse);
            float centerX = content.X + step * (i + 0.5f);

            g.FillEllipse(
                new Rectangle(
                    new Point(centerX - radius, centerY - radius),
                    new Size(radius * 2f, radius * 2f)),
                new Color((byte)(Color.A * (0.4f + 0.6f * pulse)), Color.R, Color.G, Color.B));
        }
    }

    private void DrawBar(Graphics g, Rectangle content)
    {
        float height = Math.Min(StrokeWidth * 2f, content.Height);
        var radius = new CornerRadius(height / 2f);

        var track = new Rectangle(
            new Point(content.X, content.Y + (content.Height - height) / 2f),
            new Size(content.Width, height));

        if (TrackColor.A > 0)
            g.FillRoundRectangle(track, radius, TrackColor);

        float segment = Math.Max(height, content.Width * 0.3f);

        // отрезок заходит и уходит за края, поэтому дорожку подрезаем
        float x = track.X - segment + _phase * (track.Width + segment);

        g.Save();
        g.ClipRoundRect(track, radius);

        g.FillRoundRectangle(
            new Rectangle(new Point(x, track.Y), new Size(segment, height)),
            radius, Color);

        g.Restore();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Size content = Style switch
        {
            // ряд точек шире, чем высок
            LoaderStyle.Dots => new Size(IndicatorSize * Math.Max(2, ElementCount) / 2f, IndicatorSize / 2f),

            // полоса тянется по ширине, своей у неё нет
            LoaderStyle.Bar => new Size(float.IsFinite(availableSize.Width) ? availableSize.Width : IndicatorSize * 4f,
                                        StrokeWidth * 2f),

            _ => new Size(IndicatorSize, IndicatorSize),
        };

        return ResolveSize(
            new Size(content.Width + Padding.Horizontal, content.Height + Padding.Vertical),
            availableSize);
    }

    protected override void OnStyledPropertyChanged(StyledProperty property)
    {
        if (property != IsVisibleProperty) return;

        // скрытый индикатор не должен держать тикер окна: его анимация
        // бесконечна, и сама она никогда не завершится
        if (IsVisible && IsRunning) Start();
        else this.StopAnimation(AnimationKey);
    }
}

public enum LoaderStyle
{
    /// <summary>Дуга переменной длины, вращается. Material, Windows 11.</summary>
    Ring,

    /// <summary>Лучи по кругу с затухающей прозрачностью. iOS, macOS.</summary>
    Spinner,

    /// <summary>Ряд пульсирующих точек.</summary>
    Dots,

    /// <summary>Отрезок, бегущий по дорожке. Неопределённый прогресс.</summary>
    Bar,
}