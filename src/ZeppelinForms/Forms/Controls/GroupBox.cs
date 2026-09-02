using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// Рамка с заголовком, врезанным в верхнюю линию — как GroupBox в WinForms.
/// </summary>
public class GroupBox : WrapControl, IBorderedElement
{
    private const float HeaderSideGap = 8f;
    private const float HeaderTextPadding = 4f;

    public string? Header { get; set; }

    public Color HeaderColor { get; set; } = Colors.Black;

    /// <summary>Отступ заголовка от левого края рамки.</summary>
    public float HeaderIndent { get; set; } = 10f;

    public HorizontalContentAlignment HeaderAlign { get; set; } = HorizontalContentAlignment.Left;

    public Color BorderColor { get; set; } = new Color(255, 200, 200, 200);
    public float BorderWidth { get; set; } = 1f;

    public GroupBox()
    {
        Padding = new Thickness(10, 8);
    }

    private float HeaderHeight
    {
        get
        {
            if (string.IsNullOrEmpty(Header)) return 0;

            // высота по эталонной паре, а не по самому тексту: иначе рамка
            // будет дёргаться при смене заголовка из-за выносных элементов букв
            return TextMeasurer.Current.MeasureText("Wg", EffectiveFont).Height;
        }
    }

    private float HeaderWidth => string.IsNullOrEmpty(Header)
        ? 0
        : TextMeasurer.Current.MeasureText(Header, EffectiveFont).Width;

    public override void Draw(Graphics g)
    {
        if (Background.A > 0)
            g.FillRoundRectangle(LocalBounds, CornerRadius, Background);

        float headerHeight = HeaderHeight;

        // рамка начинается на середине строки заголовка — так текст
        // визуально «врезан» в линию, а не висит над ней
        float top = headerHeight / 2f;

        var frame = new Rectangle(
            new Point(BorderWidth / 2f, top),
            new Size(
                Math.Max(0, ActualSize.Width - BorderWidth),
                Math.Max(0, ActualSize.Height - top - BorderWidth / 2f)));

        if (BorderWidth > 0)
        {
            if (string.IsNullOrEmpty(Header))
            {
                g.DrawRoundRectangle(frame, CornerRadius, BorderColor, BorderWidth);
            }
            else
            {
                DrawFrameWithGap(g, frame, headerHeight);
            }
        }

        if (string.IsNullOrEmpty(Header)) return;

        float textX = HeaderTextX(frame);

        g.DrawText(Header,
            new Rectangle(new Point(textX, 0), new Size(HeaderWidth, headerHeight)),
            HeaderColor, EffectiveFont,
            HorizontalContentAlignment.Left, VerticalContentAlignment.Center);
    }

    private float HeaderTextX(Rectangle frame) => HeaderAlign switch
    {
        HorizontalContentAlignment.Center => frame.X + (frame.Width - HeaderWidth) / 2f,
        HorizontalContentAlignment.Right => frame.X + frame.Width - HeaderWidth - HeaderIndent,
        _ => frame.X + HeaderIndent,
    };

    /// <summary>
    /// Рисует рамку из четырёх сторон, оставляя разрыв в верхней линии
    /// под заголовок. Целиком прямоугольник рисовать нельзя — линия
    /// прошла бы прямо через текст.
    /// </summary>
    private void DrawFrameWithGap(Graphics g, Rectangle frame, float headerHeight)
    {
        float textX = HeaderTextX(frame);

        float gapStart = textX - HeaderTextPadding;
        float gapEnd = textX + HeaderWidth + HeaderTextPadding;

        float y = frame.Y;
        float right = frame.X + frame.Width;
        float bottom = frame.Y + frame.Height;

        // верхняя линия двумя отрезками по бокам от заголовка
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

        var content = new Size(
            Math.Max(childDesired.Width + Padding.Horizontal, minWidth),
            childDesired.Height + Padding.Vertical + headerHeight);

        return ResolveSize(content, availableSize);
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