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
    private const float BoxSize = 16f;
    private const float Gap = 6f;

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

    public HorizontalContentAlignment HorizontalContentAlign { get; set; } = HorizontalContentAlignment.Left;
    public VerticalContentAlignment VerticalContentAlign { get; set; } = VerticalContentAlignment.Center;

    public CheckBox()
    {
        Cursor = CursorKind.Hand;
    }

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

    protected override void DrawContent(Graphics g)
    {
        var content = ContentBounds;

        float boxY = content.Y + (content.Height - BoxSize) / 2f;
        var boxRect = new Rectangle(new Point(content.X, boxY), new Size(BoxSize, BoxSize));

        // a filled box in the checked state looks closer to the system ones
        bool filled = _checkState != CheckedState.Unchecked;
        var radius = new CornerRadius(3f);

        g.FillRoundRectangle(boxRect, radius, filled ? CheckColor : BoxBackground);
        // the box is where the click goes, so the hover shows there —
        // the control's own border is usually zero-width
        Color boxBorder = filled ? CheckColor
            : IsHovered && IsEnabled && HoverBorderColor.A > 0 ? HoverBorderColor
            : BoxBorderColor;

        g.DrawRoundRectangle(boxRect, radius, boxBorder, 1.5f);

        switch (_checkState)
        {
            case CheckedState.Checked:
                DrawCheckMark(g, boxRect);
                break;

            case CheckedState.Intermediate:
                DrawDash(g, boxRect);
                break;
        }

        if (string.IsNullOrEmpty(Text)) return;

        var textRect = new Rectangle(
            new Point(content.X + BoxSize + Gap, content.Y),
            new Size(Math.Max(0, content.Width - BoxSize - Gap), content.Height));

        g.DrawText(ApplyTextTransform(Text), textRect, TextColor, EffectiveFont, this.HorizontalContentAlign, this.VerticalContentAlign);
    }

    private static void DrawCheckMark(Graphics g, Rectangle box)
    {
        // fractions of the square's side — the check mark scales together with BoxSize
        ReadOnlySpan<Point> points =
        [
            new(box.X + box.Width * 0.22f, box.Y + box.Height * 0.52f),
            new(box.X + box.Width * 0.42f, box.Y + box.Height * 0.72f),
            new(box.X + box.Width * 0.78f, box.Y + box.Height * 0.30f),
        ];

        g.DrawPolyline(points, Colors.White, box.Width * 0.14f);
    }

    private static void DrawDash(Graphics g, Rectangle box)
    {
        g.DrawLine(
            new Point(box.X + box.Width * 0.24f, box.Y + box.Height * 0.5f),
            new Point(box.X + box.Width * 0.76f, box.Y + box.Height * 0.5f),
            Colors.White,
            box.Width * 0.14f);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Size textSize = string.IsNullOrEmpty(Text)
            ? Size.Empty
            : TextMeasurer.Current.MeasureText(ApplyTextTransform(Text), EffectiveFont);

        float width = BoxSize + (textSize.Width > 0 ? Gap + textSize.Width : 0) + Padding.Horizontal;
        float height = Math.Max(BoxSize, textSize.Height) + Padding.Vertical;

        return ResolveSize(new Size(width, height), availableSize);
    }
}

public enum CheckedState : byte
{
    Unchecked,
    Intermediate,
    Checked,
}