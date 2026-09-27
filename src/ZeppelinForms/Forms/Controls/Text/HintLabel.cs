using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Layout;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls.Text;

/// <summary>
/// Text with a dashed underline: on click it expands an explanation.
/// </summary>
public class HintLabel : DecoratedControl
{
    private readonly FlyoutHost _flyout;

    public string? Text
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // the label's size is computed from its text
            Invalidate();
        }
    }

    /// <summary>The explanation. May be multi-line via \n.</summary>
    public string? Hint { get; set; }

    /// <summary>Custom hint content instead of plain text.</summary>
    public Func<UIElement>? HintContent { get; set; }

    public float MaxHintWidth { get; set; } = 320f;

    public Color HoverTextColor { get; set; } = new Color(255, 0x0D, 0x6E, 0xFD);
    public Color UnderlineColor { get; set; } = new Color(255, 150, 150, 150);

    public float DashLength { get; set; } = 3f;
    public float DashGap { get; set; } = 2f;

    public FlyoutPlacement Placement { get; set; } = FlyoutPlacement.Bottom;

    public bool IsHintOpen => _flyout.IsOpen;

    public HintLabel()
    {
        Cursor = CursorKind.Hand;
        SetControlDefault(PaddingProperty, new(0, 2));

        _flyout = new FlyoutHost(this);
        _flyout.Closed += (_, _) => InvalidateVisual();
    }

    protected override void DrawContent(Graphics g)
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
        // the dashes are drawn as segments: DrawLine can't do dashing,
        // and adding a dash filter to Graphics for this would be excessive
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