using System.Globalization;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

public partial class NumericUpDown : TextInputControl
{
    private const float ButtonWidth = 18f;

    /// <summary>The value as it was assigned, before coercing into the range.</summary>
    /// <remarks>
    /// Coercing on read makes the result independent of the order of assignments:
    /// in <c>new NumericUpDown { Value = 150, Maximum = 200 }</c> the value used to
    /// be clamped by the default maximum of 100 before the real one arrived.
    /// </remarks>
    private decimal _requested;

    private bool _hoverUp;
    private bool _hoverDown;

    private string? _editText;
    private bool _isEditing;
    private int _caretIndex;

    public decimal Minimum
    {
        get;
        set
        {
            if (field == value) return;

            decimal before = Value;
            field = value;
            OnRangeChanged(before);
        }
    } = 0;

    public decimal Maximum
    {
        get;
        set
        {
            if (field == value) return;

            decimal before = Value;
            field = value;
            OnRangeChanged(before);
        }
    } = 100;

    public decimal Step { get; set; } = 1;

    public int DecimalPlaces
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // the width is measured by the formatted boundary values
            Invalidate();
        }
    }

    /// <summary>Allow entering the value from the keyboard.</summary>
    public bool IsEditable { get; set; } = true;

    public decimal Value
    {
        get => Coerce(_requested);
        set
        {
            decimal before = Value;
            _requested = value;

            if (before == Value) return;

            OnValueChanged();
        }
    }

    // while the range is being reassigned, Minimum may briefly exceed Maximum:
    // Math.Clamp throws on that, so the value is left as is until the range is valid
    private decimal Coerce(decimal value) =>
        Minimum <= Maximum ? Math.Clamp(value, Minimum, Maximum) : value;

    /// <summary>The range changed. If that moved the coerced value,
    /// whoever listens to ValueChanged must know it.</summary>
    private void OnRangeChanged(decimal before)
    {
        if (before != Value)
            OnValueChanged();

        // the width is measured by the boundary values
        Invalidate();
    }

    private void OnValueChanged()
    {
        // in edit mode _editText goes to the screen, so it must be updated
        // too — otherwise the value would change unnoticed
        if (_isEditing)
        {
            _editText = Formatted;
            _caretIndex = _editText.Length;
            ResetCaretBlink();
        }

        ValueChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public event EventHandler? ValueChanged;

    [Styled(Category = "Buttons")]
    public partial Color ButtonColor { get; set; }
    private static Color ButtonColorDefault => new(255, 240, 240, 240);

    [Styled(Category = "Buttons")]
    public partial Color ButtonHoverColor { get; set; }
    private static Color ButtonHoverColorDefault => new(255, 220, 220, 220);

    /// <summary>The color of the arrows on the buttons. A styled property, so the
    /// theme sets it: the fixed black disappeared on the dark theme's buttons.</summary>
    [Styled(Category = "Buttons")]
    public partial Color ArrowColor { get; set; }
    private static Color ArrowColorDefault => Colors.Black;

    public NumericUpDown()
    {
        SetControlDefault(BackgroundProperty, Colors.White);
        SetControlDefault(PaddingProperty, new(6, 3));
        SetControlDefault(BorderColorProperty, Colors.Black);
        SetControlDefault(BorderWidthProperty, 1f);

        // the text cursor from TextInputControl doesn't fit here: the field is
        // mostly controlled by the buttons, editing is secondary
        Cursor = CursorKind.Arrow;
    }

    private string Formatted => Value.ToString($"F{DecimalPlaces}");

    private string DisplayText => _isEditing ? _editText ?? string.Empty : Formatted;

    private static char DecimalSeparator =>
        CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator[0];

    private Rectangle UpButtonRect => new(
        new Point(ActualSize.Width - ButtonWidth, 0),
        new Size(ButtonWidth, ActualSize.Height / 2f));

    private Rectangle DownButtonRect => new(
        new Point(ActualSize.Width - ButtonWidth, ActualSize.Height / 2f),
        new Size(ButtonWidth, ActualSize.Height / 2f));

    protected override void DrawContent(Graphics g)
    {
        var textRect = new Rectangle(
            new Point(Padding.Left, Padding.Top),
            new Size(
                Math.Max(0, ActualSize.Width - ButtonWidth - Padding.Horizontal),
                Math.Max(0, ActualSize.Height - Padding.Vertical)));

        g.DrawText(DisplayText, textRect, TextColor, EffectiveFont,
            HorizontalContentAlignment.Right, VerticalContentAlignment.Center);

        if (_isEditing && IsFocused && CaretVisible)
        {
            string text = DisplayText;

            float textWidth = TextMeasurer.Current.MeasureText(text, EffectiveFont).Width;
            float caretOffset = TextMeasurer.Current.MeasureTextWidth(text, _caretIndex, EffectiveFont);

            // the text is right-aligned, so the caret is counted from the right edge
            float right = ActualSize.Width - ButtonWidth - Padding.Right;

            g.FillRectangle(
                new Rectangle(
                    new Point(right - textWidth + caretOffset, Padding.Top + 2),
                    new Size(1f, Math.Max(0, ActualSize.Height - Padding.Vertical - 4))),
                TextColor);
        }

        g.FillRectangle(UpButtonRect, _hoverUp ? ButtonHoverColor : ButtonColor);
        g.FillRectangle(DownButtonRect, _hoverDown ? ButtonHoverColor : ButtonColor);

        DrawArrow(g, UpButtonRect, up: true);
        DrawArrow(g, DownButtonRect, up: false);
    }

    private void DrawArrow(Graphics g, Rectangle area, bool up)
    {
        float cx = area.X + area.Width / 2f;
        float cy = area.Y + area.Height / 2f;
        float w = area.Width * 0.28f;
        float h = area.Height * 0.16f;

        ReadOnlySpan<Point> points = up
            ? [new(cx - w, cy + h), new(cx, cy - h), new(cx + w, cy + h)]
            : [new(cx - w, cy - h), new(cx, cy + h), new(cx + w, cy - h)];

        g.DrawPolyline(points, ArrowColor, 1.5f);
    }

    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        Point abs = GetAbsolutePosition();
        var local = new Point(e.Location.X - abs.X, e.Location.Y - abs.Y);

        bool up = Contains(UpButtonRect, local);
        bool down = Contains(DownButtonRect, local);

        if (up == _hoverUp && down == _hoverDown) return;

        _hoverUp = up;
        _hoverDown = down;
        InvalidateVisual();
    }

    protected override void OnMouseExit(MouseMoveEventArgs e)
    {
        _hoverUp = _hoverDown = false;
        InvalidateVisual();
    }

    protected override void OnClick(MouseClickEventArgs e)
    {
        Point abs = GetAbsolutePosition();
        var local = new Point(e.Location.X - abs.X, e.Location.Y - abs.Y);

        if (Contains(UpButtonRect, local)) Value += Step;
        else if (Contains(DownButtonRect, local)) Value -= Step;

        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        // the form delivers the wheel to disabled elements too
        if (!IsEnabled) return;

        Value += Step * Math.Sign(e.Delta);
        e.Handled = true;
    }

    private void BeginEdit()
    {
        if (!IsEditable) return;

        _isEditing = true;
        _editText = Formatted;
        _caretIndex = _editText.Length;

        ResetCaretBlink();
        InvalidateVisual();
    }

    private void CommitEdit()
    {
        if (!_isEditing) return;

        _isEditing = false;

        if (decimal.TryParse(_editText, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal parsed))
            Value = Math.Round(parsed, DecimalPlaces, MidpointRounding.AwayFromZero);

        // didn't parse — silently return the previous value,
        // there is no reason to crash the application over a typo
        _editText = null;
        InvalidateVisual();
    }

    protected override void OnGotFocus()
    {
        base.OnGotFocus();
        BeginEdit();
    }

    protected override void OnLostFocus()
    {
        CommitEdit();
        base.OnLostFocus();
    }

    protected override void OnTextInput(char c)
    {
        if (!IsEditable || !_isEditing) return;

        bool isDigit = char.IsAsciiDigit(c);
        bool isSeparator = (c == '.' || c == ',' || c == DecimalSeparator) && DecimalPlaces > 0;
        bool isMinus = c == '-' && _caretIndex == 0 && Minimum < 0
            && !(_editText?.StartsWith('-') ?? false);

        if (!isDigit && !isSeparator && !isMinus) return;

        // a separator is already there — a second one is not needed
        if (isSeparator && (_editText?.Contains(DecimalSeparator) ?? false)) return;

        // a dot from the keyboard is brought to the current culture's separator,
        // otherwise decimal.TryParse won't accept it
        char inserted = isSeparator ? DecimalSeparator : c;

        _editText = (_editText ?? string.Empty).Insert(_caretIndex, inserted.ToString());
        _caretIndex++;

        ResetCaretBlink();
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_isEditing && HandleEditingKey(e))
        {
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Up: Value += Step; e.Handled = true; break;
            case Key.Down: Value -= Step; e.Handled = true; break;
            case Key.Home: Value = Minimum; e.Handled = true; break;
            case Key.End: Value = Maximum; e.Handled = true; break;
        }
    }

    private bool HandleEditingKey(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                CommitEdit();
                BeginEdit();   // go right back into edit mode
                return true;

            case Key.Escape:
                _editText = Formatted;
                _caretIndex = _editText.Length;
                break;

            case Key.Backspace when _caretIndex > 0:
                _editText = _editText!.Remove(_caretIndex - 1, 1);
                _caretIndex--;
                break;

            case Key.Delete when _caretIndex < (_editText?.Length ?? 0):
                _editText = _editText!.Remove(_caretIndex, 1);
                break;

            case Key.Left:
                _caretIndex = Math.Max(0, _caretIndex - 1);
                break;

            case Key.Right:
                _caretIndex = Math.Min(_editText?.Length ?? 0, _caretIndex + 1);
                break;

            default:
                return false;
        }

        ResetCaretBlink();
        InvalidateVisual();
        return true;
    }

    private static bool Contains(Rectangle rect, Point p) =>
        p.X >= rect.X && p.X <= rect.X + rect.Width &&
        p.Y >= rect.Y && p.Y <= rect.Y + rect.Height;

    protected override Size MeasureOverride(Size availableSize)
    {
        // measure by the widest of the boundary values, so that the field
        // doesn't jump in width while stepping
        Size minSize = TextMeasurer.Current.MeasureText(Minimum.ToString($"F{DecimalPlaces}"), EffectiveFont);
        Size maxSize = TextMeasurer.Current.MeasureText(Maximum.ToString($"F{DecimalPlaces}"), EffectiveFont);

        Size textSize = minSize.Width >= maxSize.Width ? minSize : maxSize;

        return ResolveSize(
            new Size(
                textSize.Width + ButtonWidth + Padding.Horizontal + 8,
                textSize.Height + Padding.Vertical + 6),
            availableSize);
    }
}