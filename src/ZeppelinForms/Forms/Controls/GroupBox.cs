using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Рамка с заголовком, врезанным в верхнюю линию — как GroupBox в WinForms.
/// </summary>
public class GroupBox : DecoratedWrapControl
{
    private const float HeaderSideGap = 8f;
    private const float HeaderTextPadding = 4f;

    public string? Header { get; set; }

    public Color HeaderColor { get; set; } = Colors.Black;

    /// <summary>Отступ заголовка от левого края рамки.</summary>
    public float HeaderIndent { get; set; } = 10f;

    public HorizontalContentAlignment HeaderAlign { get; set; } = HorizontalContentAlignment.Left;

    public GroupBox()
    {
        Padding = new Thickness(10, 8);
        BorderColor = new Color(255, 200, 200, 200);
        BorderWidth = 1f;
    }

    private float HeaderHeight
    {
        get
        {
            if (string.IsNullOrEmpty(Header)) return 0;

            // высота по эталонной паре, а не по самому тексту: иначе рамка
            // дёргается при смене заголовка из-за выносных элементов букв
            return TextMeasurer.Current.MeasureText("Wg", EffectiveFont).Height;
        }
    }

    private float HeaderWidth => string.IsNullOrEmpty(Header)
        ? 0
        : TextMeasurer.Current.MeasureText(Header, EffectiveFont).Width;

    private Rectangle Frame
    {
        get
        {
            // рамка начинается на середине строки заголовка — так текст
            // визуально «врезан» в линию, а не висит над ней
            float top = HeaderHeight / 2f;

            return new Rectangle(
                new Point(BorderWidth / 2f, top),
                new Size(
                    Math.Max(0, ActualSize.Width - BorderWidth),
                    Math.Max(0, ActualSize.Height - top - BorderWidth / 2f)));
        }
    }

    /// <summary>Фон рисуем сами, по прямоугольнику рамки: базовый залил бы
    /// и полосу заголовка над ней.</summary>
    protected override Color CurrentBackground => Colors.Transparent;

    protected override void DrawContent(Graphics g)
    {
        if (Background.A > 0)
            g.FillRoundRectangle(Frame, CornerRadius, Background);
    }

    protected override void DrawDecoration(Graphics g)
    {
        Rectangle frame = Frame;

        if (BorderWidth > 0 && BorderColor.A > 0)
        {
            if (string.IsNullOrEmpty(Header))
                g.DrawRoundRectangle(frame, CornerRadius, BorderColor, BorderWidth);
            else
                DrawFrameWithGap(g, frame);
        }

        if (string.IsNullOrEmpty(Header)) return;

        g.DrawText(Header,
            new Rectangle(new Point(HeaderTextX(frame), 0), new Size(HeaderWidth, HeaderHeight)),
            HeaderColor, EffectiveFont,
            HorizontalContentAlignment.Left, VerticalContentAlignment.Center);
    }

    // базовая рамка не подходит: её рисует DrawDecoration с разрывом
    protected override Color CurrentBorderColor => Colors.Transparent;

    private float HeaderTextX(Rectangle frame) => HeaderAlign switch
    {
        HorizontalContentAlignment.Center => frame.X + (frame.Width - HeaderWidth) / 2f,
        HorizontalContentAlignment.Right => frame.X + frame.Width - HeaderWidth - HeaderIndent,
        _ => frame.X + HeaderIndent,
    };

    /// <summary>
    /// Рамка из четырёх сторон с разрывом в верхней линии под заголовок:
    /// целиком прямоугольник прошёл бы прямо через текст.
    /// </summary>
    private void DrawFrameWithGap(Graphics g, Rectangle frame)
    {
        float textX = HeaderTextX(frame);

        float gapStart = textX - HeaderTextPadding;
        float gapEnd = textX + HeaderWidth + HeaderTextPadding;

        float y = frame.Y;
        float right = frame.X + frame.Width;
        float bottom = frame.Y + frame.Height;

        if (gapStart > frame.X)
            g.DrawLine(new Point(frame.X, y), new Point(gapStart, y), BorderColor, BorderWidth);

        if (gapEnd < right)
            g.DrawLine(new Point(gapEnd, y), new Point(right, y), BorderColor, BorderWidth);

        g.DrawLine(new Point(frame.X, y), new Point(frame.X, bottom), BorderColor, BorderWidth);
        g.DrawLine(new Point(right, y), new Point(right, bottom), BorderColor, BorderWidth);
        g.DrawLine(new Point(frame.X, bottom), new Point(right, bottom), BorderColor, BorderWidth);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        float headerHeight = HeaderHeight;

        var inner = new Size(
            Math.Max(0, availableSize.Width - Padding.Horizontal),
            Math.Max(0, availableSize.Height - Padding.Vertical - headerHeight));

        Size childDesired = Size.Empty;

        if (Child is not null)
        {
            Child.Measure(inner);
            childDesired = Child.DesiredSize;
        }

        // ширина не меньше заголовка с отступами, иначе текст вылезет за рамку
        float minWidth = HeaderWidth + HeaderIndent + HeaderSideGap * 2;

        return ResolveSize(
            new Size(
                Math.Max(childDesired.Width + Padding.Horizontal, minWidth),
                childDesired.Height + Padding.Vertical + headerHeight),
            availableSize);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child is null) return finalSize;

        float headerHeight = HeaderHeight;

        Child.Arrange(new Rectangle(
            new Point(Padding.Left, Padding.Top + headerHeight),
            new Size(
                Math.Max(0, finalSize.Width - Padding.Horizontal),
                Math.Max(0, finalSize.Height - Padding.Vertical - headerHeight))));

        return finalSize;
    }
}