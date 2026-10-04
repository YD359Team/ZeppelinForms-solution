using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

public partial class CheckBox : InteractiveControl, ITextElement
{
    private const float Gap = 6f;

    /// <summary>How far the focus ring runs outside the box.</summary>
    private const float FocusRingGap = 2f;

    private CheckedState _checkState = CheckedState.Unchecked;
    private bool _isThreeState;

    public bool IsThreeState
    {
        get => _isThreeState;
        set
        {
            if (_isThreeState == value) return;

            _isThreeState = value;

            // the third state was turned off while we are in it — bring it to a valid one
            if (!value && _checkState == CheckedState.Intermediate)
                SetState(CheckedState.Unchecked);
        }
    }

    public bool IsChecked
    {
        get => _checkState == CheckedState.Checked;
        set => SetState(value ? CheckedState.Checked : CheckedState.Unchecked);
    }

    public CheckedState CheckedState
    {
        get => _checkState;
        set
        {
            if (!IsThreeState && value == CheckedState.Intermediate)
                throw new ArgumentException(
                    $"The {value} state is allowed only with IsThreeState = true.", nameof(value));

            SetState(value);
        }
    }

    public event EventHandler? CheckedChanged;

    public string? Text
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // the check box's size is computed from its text
            Invalidate();
        }
    }

    [Styled(Category = "Box")]
    public partial Color BoxBorderColor { get; set; }
    private static Color BoxBorderColorDefault => Colors.Black;

    [Styled(Category = "Box")]
    public partial Color BoxBackground { get; set; }
    private static Color BoxBackgroundDefault => Colors.White;

    [Styled(Category = "Box")]
    public partial Color CheckColor { get; set; }
    private static Color CheckColorDefault => new(255, 0x0D, 0x6E, 0xFD);

    /// <summary>The check mark and the dash on the filled box. Was a hard-coded
    /// white, which is lost on a light accent — the dark Fluent theme has one.</summary>
    [Styled(Category = "Box")]
    public partial Color CheckGlyphColor { get; set; }
    private static Color CheckGlyphColorDefault => Colors.White;

    // ===== geometry =====
    //
    // Constants before 0.13: the classic look drew a 16 px box rounded to 3,
    // while Fluent's is 20 px rounded to 4 with a thinner stroke.

    /// <summary>The side of the square box.</summary>
    [Styled(Category = "Box", AffectsLayout = true)]
    public partial float BoxSize { get; set; }
    private static float BoxSizeDefault => 16f;

    [Styled(Category = "Box")]
    public partial CornerRadius BoxCornerRadius { get; set; }
    private static CornerRadius BoxCornerRadiusDefault => new(3f);

    [Styled(Category = "Box")]
    public partial float BoxBorderWidth { get; set; }
    private static float BoxBorderWidthDefault => 1.5f;

    public HorizontalContentAlignment HorizontalContentAlign { get; set; } = HorizontalContentAlignment.Left;
    public VerticalContentAlignment VerticalContentAlign { get; set; } = VerticalContentAlignment.Center;

    public CheckBox()
    {
        Cursor = CursorKind.Hand;

        // the ring around a small box reads better a little heavier than
        // the hairline one around a button — the same as ToggleSwitch's
        SetControlDefault(FocusRingThicknessProperty, 1.5f);
    }

    /// <summary>The ring runs around the box, and the box stands at the very edge
    /// of the control when there is no padding.</summary>
    protected override Thickness VisualOverflow => new(FocusRingGap + FocusRingThickness);

    private void SetState(CheckedState state)
    {
        if (_checkState == state) return;

        _checkState = state;
        CheckedChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    protected override void OnClick(MouseClickEventArgs e)
    {
        // Space and Enter come here too, bypassing hit testing:
        // a disabled check box must not toggle from the keyboard
        if (!IsEnabled) return;

        // as in WinForms: without the third state — a toggle,
        // with it — the cycle Unchecked → Checked → Intermediate
        SetState(_checkState switch
        {
            CheckedState.Unchecked => CheckedState.Checked,
            CheckedState.Checked => IsThreeState ? CheckedState.Intermediate : CheckedState.Unchecked,
            _ => CheckedState.Unchecked,
        });

        e.Handled = true;
    }

    /// <summary>The box, centered vertically at the start of the content.
    /// Shared by the content and the focus ring, so the two never drift apart.</summary>
    private Rectangle BoxRect
    {
        get
        {
            Rectangle content = ContentBounds;
            float size = BoxSize;

            return new Rectangle(
                new Point(content.X, content.Y + (content.Height - size) / 2f),
                new Size(size, size));
        }
    }

    protected override void DrawContent(Graphics g)
    {
        var content = ContentBounds;
        float boxSize = BoxSize;
        Rectangle boxRect = BoxRect;

        // a filled box in the checked state looks closer to the system ones
        bool filled = _checkState != CheckedState.Unchecked;
        CornerRadius radius = BoxCornerRadius;

        g.FillRoundRectangle(boxRect, radius, filled ? CheckColor : BoxBackground);
        // the box is where the click goes, so the hover shows there —
        // the control's own border is usually zero-width
        Color boxBorder = filled ? CheckColor
            : IsHovered && IsEnabled && HoverBorderColor.A > 0 ? HoverBorderColor
            : BoxBorderColor;

        if (BoxBorderWidth > 0f)
            g.DrawRoundRectangle(boxRect, radius, boxBorder, BoxBorderWidth);

        switch (_checkState)
        {
            case CheckedState.Checked:
                DrawCheckMark(g, boxRect, CheckGlyphColor);
                break;

            case CheckedState.Intermediate:
                DrawDash(g, boxRect, CheckGlyphColor);
                break;
        }

        if (string.IsNullOrEmpty(Text)) return;

        var textRect = new Rectangle(
            new Point(content.X + boxSize + Gap, content.Y),
            new Size(Math.Max(0, content.Width - boxSize - Gap), content.Height));

        g.DrawText(ApplyTextTransform(Text), textRect, TextColor, EffectiveFont, this.HorizontalContentAlign, this.VerticalContentAlign);
        DrawAccessKeyUnderline(g, Text, textRect, TextColor, this.HorizontalContentAlign, this.VerticalContentAlign);
    }

    /// <summary>The focus ring goes around the box, not around the whole control:
    /// the box is what Space toggles, and a frame around the caption as well
    /// would run through the text when there is no padding.</summary>
    protected override void DrawDecoration(Graphics g)
    {
        DrawFocusRing(
            g,
            Grow(BoxRect, FocusRingGap),
            Grow(BoxCornerRadius, FocusRingGap),
            CheckColor);
    }

    private static void DrawCheckMark(Graphics g, Rectangle box, Color color)
    {
        // fractions of the square's side — the check mark scales together with BoxSize
        ReadOnlySpan<Point> points =
        [
            new(box.X + box.Width * 0.22f, box.Y + box.Height * 0.52f),
            new(box.X + box.Width * 0.42f, box.Y + box.Height * 0.72f),
            new(box.X + box.Width * 0.78f, box.Y + box.Height * 0.30f),
        ];

        g.DrawPolyline(points, color, box.Width * 0.14f);
    }

    private static void DrawDash(Graphics g, Rectangle box, Color color)
    {
        g.DrawLine(
            new Point(box.X + box.Width * 0.24f, box.Y + box.Height * 0.5f),
            new Point(box.X + box.Width * 0.76f, box.Y + box.Height * 0.5f),
            color,
            box.Width * 0.14f);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Size textSize = string.IsNullOrEmpty(Text)
            ? Size.Empty
            : TextMeasurer.Current.MeasureText(ApplyTextTransform(Text), EffectiveFont);

        float boxSize = BoxSize;

        float width = boxSize + (textSize.Width > 0 ? Gap + textSize.Width : 0) + Padding.Horizontal;
        float height = Math.Max(boxSize, textSize.Height) + Padding.Vertical;

        return ResolveSize(new Size(width, height), availableSize);
    }
}

public enum CheckedState : byte
{
    Unchecked,
    Intermediate,
    Checked,
}