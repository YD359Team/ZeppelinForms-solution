using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Layout;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls.Text;

/// <summary>
/// Текст с пунктирным подчёркиванием: по клику раскрывает пояснение.
/// </summary>
public class HintLabel : UnitControl
{
    private readonly FlyoutHost _flyout;

    public string? Text { get; set; }

    /// <summary>Пояснение. Может быть многострочным через \n.</summary>
    public string? Hint { get; set; }

    /// <summary>Своё содержимое подсказки вместо простого текста.</summary>
    public Func<UIElement>? HintContent { get; set; }

    public float MaxHintWidth { get; set; } = 320f;

    public Color TextColor { get; set; } = Colors.Black;
    public Color HoverTextColor { get; set; } = new Color(255, 0x0D, 0x6E, 0xFD);
    public Color UnderlineColor { get; set; } = new Color(255, 150, 150, 150);

    public float DashLength { get; set; } = 3f;
    public float DashGap { get; set; } = 2f;

    public FlyoutPlacement Placement { get; set; } = FlyoutPlacement.Bottom;

    public bool IsHintOpen => _flyout.IsOpen;

    public HintLabel()
    {
        Cursor = CursorKind.Hand;
        Padding = new Thickness(0, 2);

        _flyout = new FlyoutHost(this);
        _flyout.Closed += (_, _) => InvalidateVisual();
    }

    public override void Draw(Graphics g)
    {
        if (string.IsNullOrEmpty(Text)) return;

        Rectangle content = ContentBounds;
        Color color = IsHovered || _flyout.IsOpen ? HoverTextColor : TextColor;

        g.DrawText(Text, content, color, EffectiveFont,
            HorizontalContentAlignment.Left, VerticalContentAlignment.Center);

        float textWidth = TextMeasurer.Current.MeasureText(Text, EffectiveFont).Width;
        float lineHeight = TextMeasurer.Current.MeasureText("Wg", EffectiveFont).Height;
        float y = content.Y + (content.Height + lineHeight) / 2f;

        DrawDashedLine(g, content.X, content.X + textWidth, y,
            IsHovered || _flyout.IsOpen ? HoverTextColor : UnderlineColor);
    }

    private void DrawDashedLine(Graphics g, float from, float to, float y, Color color)
    {
        // пунктир рисуем отрезками: DrawLine не умеет штриховку,
        // а заводить ради этого dash-фильтр в Graphics избыточно
        float x = from;

        while (x < to)
        {
            float end = Math.Min(x + DashLength, to);
            g.DrawLine(new Point(x, y), new Point(end, y), color, 1f);
            x = end + DashGap;
        }
    }

    protected override void OnMouseEnter(MouseMoveEventArgs e) => InvalidateVisual();
    protected override void OnMouseExit(MouseMoveEventArgs e) => InvalidateVisual();

    protected override void OnClick(MouseClickEventArgs e)
    {
        e.Handled = true;
        _flyout.Toggle(BuildHint, Placement);
        InvalidateVisual();
    }

    private UIElement BuildHint()
    {
        UIElement inner = HintContent?.Invoke() ?? new Label
        {
            Text = Hint ?? string.Empty,
            TextColor = App.Theme.Colors.Text,
            HorizontalContentAlign = HorizontalContentAlignment.Left,
            VerticalContentAlign = VerticalContentAlignment.Top,
            Size = new Size(MaxHintWidth, float.NaN),
        };

        return new Border
        {
            Background = App.Theme.Colors.Surface,
            BorderColor = App.Theme.Colors.Border,
            BorderWidth = 1,
            CornerRadius = new CornerRadius(4f),
            Padding = new Thickness(10, 8),
            BoxShadow = BoxShadow.Medium,
            Child = inner,
        };
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Size textSize = string.IsNullOrEmpty(Text)
            ? Size.Empty
            : TextMeasurer.Current.MeasureText(Text, EffectiveFont);

        return ResolveSize(
            new Size(textSize.Width + Padding.Horizontal, textSize.Height + Padding.Vertical + 2),
            availableSize);
    }
}