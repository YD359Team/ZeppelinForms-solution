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
    private const float Gap = 6f;

    /// <summary>How far the focus ring runs outside the circle.</summary>
    private const float FocusRingGap = 2f;

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

    /// <summary>The circle's fill when checked. Fluent fills the checked circle with
    /// the accent and puts a light dot on it — the reverse of the classic look.
    /// Transparent — the same fill as unchecked.</summary>
    [Styled(Category = "Box")]
    public partial Color CheckedCircleBackground { get; set; }
    private static Color CheckedCircleBackgroundDefault => Colors.Transparent;

    /// <summary>The dot of the checked state. Transparent — <see cref="CheckColor"/>.</summary>
    [Styled(Category = "Box")]
    public partial Color DotColor { get; set; }
    private static Color DotColorDefault => Colors.Transparent;

    // ===== geometry =====
    //
    // Constants before 0.13: the classic look drew a 16 px circle with an 8 px
    // dot, while Fluent's is 20 px with a 12 px dot and a thinner stroke.

    /// <summary>The diameter of the circle.</summary>
    [Styled(Category = "Box", AffectsLayout = true)]
    public partial float CircleSize { get; set; }
    private static float CircleSizeDefault => 16f;

    [Styled(Category = "Box")]
    public partial float CircleBorderWidth { get; set; }
    private static float CircleBorderWidthDefault => 1.5f;

    /// <summary>The diameter of the checked dot.</summary>
    [Styled(Category = "Box")]
    public partial float DotSize { get; set; }
    private static float DotSizeDefault => 8f;

    public HorizontalContentAlignment HorizontalContentAlign { get; set; } = HorizontalContentAlignment.Left;
    public VerticalContentAlignment VerticalContentAlign { get; set; } = VerticalContentAlignment.Center;

    public RadioButton()
    {
        Cursor = CursorKind.Hand;

        // heavier than a button's ring, as with CheckBox and ToggleSwitch
        SetControlDefault(FocusRingThicknessProperty, 1.5f);
    }

    /// <summary>The ring runs around the circle, and the circle stands at the very
    /// edge of the control when there is no padding.</summary>
    protected override Thickness VisualOverflow => new(FocusRingGap + FocusRingThickness);

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

    /// <summary>The circle, centered vertically at the start of the content.
    /// Shared by the content and the focus ring, so the two never drift apart.</summary>
    private Rectangle CircleRect
    {
        get
        {
            Rectangle content = ContentBounds;
            float size = CircleSize;

            return new Rectangle(
                new Point(content.X, content.Y + (content.Height - size) / 2f),
                new Size(size, size));
        }
    }

    protected override void DrawContent(Graphics g)
    {
        var content = ContentBounds;
        float circleSize = CircleSize;
        Rectangle circleRect = CircleRect;

        // the circle is where the click goes, so the hover shows there —
        // the control's own border is usually zero-width
        Color border = IsChecked ? CheckColor
            : IsHovered && IsEnabled && HoverBorderColor.A > 0 ? HoverBorderColor
            : CircleBorderColor;

        Color fill = IsChecked && CheckedCircleBackground.A > 0
            ? CheckedCircleBackground
            : CircleBackground;

        g.FillEllipse(circleRect, fill);

        if (CircleBorderWidth > 0f)
            g.DrawEllipse(circleRect, border, CircleBorderWidth);

        if (IsChecked)
        {
            float dotSize = Math.Clamp(DotSize, 0f, circleSize);
            float inset = (circleSize - dotSize) / 2f;

            var dot = new Rectangle(
                new Point(circleRect.X + inset, circleRect.Y + inset),
                new Size(dotSize, dotSize));

            g.FillEllipse(dot, DotColor.A > 0 ? DotColor : CheckColor);
        }

        if (string.IsNullOrEmpty(Text)) return;

        var textRect = new Rectangle(
            new Point(content.X + circleSize + Gap, content.Y),
            new Size(Math.Max(0, content.Width - circleSize - Gap), content.Height));

        g.DrawText(ApplyTextTransform(Text), textRect, TextColor, EffectiveFont,
            this.HorizontalContentAlign, this.VerticalContentAlign);
    }

    /// <summary>The focus ring goes around the circle — a round ring
    /// for a round control, as with the box of a CheckBox.</summary>
    protected override void DrawDecoration(Graphics g)
    {
        Rectangle ring = Grow(CircleRect, FocusRingGap);

        DrawFocusRing(g, ring, new CornerRadius(ring.Width / 2f), CheckColor);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Size textSize = string.IsNullOrEmpty(Text)
            ? Size.Empty
            : TextMeasurer.Current.MeasureText(ApplyTextTransform(Text), EffectiveFont);

        float circleSize = CircleSize;

        float width = circleSize + (textSize.Width > 0 ? Gap + textSize.Width : 0) + Padding.Horizontal;
        float height = Math.Max(circleSize, textSize.Height) + Padding.Vertical;

        return ResolveSize(new Size(width, height), availableSize);
    }
}