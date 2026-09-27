using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

public partial class RadioButton : InteractiveControl, ITextElement
{
    private const float CircleSize = 16f;
    private const float Gap = 6f;

    public bool IsChecked { get; private set; }
    public string? GroupName { get; set; }

    public event EventHandler? CheckedChanged;

    public string? Text
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // the radio button's size is computed from its text
            Invalidate();
        }
    }

    [Styled(Category = "Box")]
    public partial Color CircleBorderColor { get; set; }
    private static Color CircleBorderColorDefault => Colors.Black;

    /// <summary>The circle's fill. Was a hard-coded white, which stayed white
    /// in the dark theme.</summary>
    [Styled(Category = "Box")]
    public partial Color CircleBackground { get; set; }
    private static Color CircleBackgroundDefault => Colors.White;

    [Styled(Category = "Box")]
    public partial Color CheckColor { get; set; }
    private static Color CheckColorDefault => new(255, 0x0D, 0x6E, 0xFD);

    public HorizontalContentAlignment HorizontalContentAlign { get; set; } = HorizontalContentAlignment.Left;
    public VerticalContentAlignment VerticalContentAlign { get; set; } = VerticalContentAlignment.Center;

    public RadioButton()
    {
        Cursor = CursorKind.Hand;
    }

    protected override void OnClick(MouseClickEventArgs e)
    {
        // Space and Enter come here too, bypassing hit testing:
        // a disabled radio button must not switch from the keyboard
        if (!IsEnabled) return;

        if (!IsChecked)
            SetChecked(true);

        e.Handled = true;
    }

    public void SetChecked(bool value)
    {
        if (IsChecked == value) return;

        if (value)
            UncheckSiblings();

        IsChecked = value;
        CheckedChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private void UncheckSiblings()
    {
        if (Parent is not PanelControl panel) return;

        foreach (RadioButton sibling in panel.Children.OfType<RadioButton>())
            if (!ReferenceEquals(sibling, this) && sibling.GroupName == GroupName)
                sibling.SetChecked(false);
    }

    protected override void DrawContent(Graphics g)
    {
        var content = ContentBounds;

        float circleY = content.Y + (content.Height - CircleSize) / 2f;
        var circleRect = new Rectangle(new Point(content.X, circleY), new Size(CircleSize, CircleSize));

        // the circle is where the click goes, so the hover shows there —
        // the control's own border is usually zero-width
        Color border = IsChecked ? CheckColor
            : IsHovered && IsEnabled && HoverBorderColor.A > 0 ? HoverBorderColor
            : CircleBorderColor;

        g.FillEllipse(circleRect, CircleBackground);
        g.DrawEllipse(circleRect, border, 1.5f);

        if (IsChecked)
        {
            const float inset = 4f;

            var dot = new Rectangle(
                new Point(circleRect.X + inset, circleRect.Y + inset),
                new Size(CircleSize - inset * 2, CircleSize - inset * 2));

            g.FillEllipse(dot, CheckColor);
        }

        if (string.IsNullOrEmpty(Text)) return;

        var textRect = new Rectangle(
            new Point(content.X + CircleSize + Gap, content.Y),
            new Size(Math.Max(0, content.Width - CircleSize - Gap), content.Height));

        g.DrawText(ApplyTextTransform(Text), textRect, TextColor, EffectiveFont,
            this.HorizontalContentAlign, this.VerticalContentAlign);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Size textSize = string.IsNullOrEmpty(Text)
            ? Size.Empty
            : TextMeasurer.Current.MeasureText(ApplyTextTransform(Text), EffectiveFont);

        float width = CircleSize + (textSize.Width > 0 ? Gap + textSize.Width : 0) + Padding.Horizontal;
        float height = Math.Max(CircleSize, textSize.Height) + Padding.Vertical;

        return ResolveSize(new Size(width, height), availableSize);
    }
}