using System.Globalization;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

public class NumericUpDown : TextInputControl
{
    private const float ButtonWidth = 18f;

    private decimal _value;
    private bool _hoverUp;
    private bool _hoverDown;

    private string? _editText;
    private bool _isEditing;
    private int _caretIndex;

    public decimal Minimum { get; set; } = 0;
    public decimal Maximum { get; set; } = 100;
    public decimal Step { get; set; } = 1;
    public int DecimalPlaces { get; set; }

    /// <summary>Разрешить ввод значения с клавиатуры.</summary>
    public bool IsEditable { get; set; } = true;

    public decimal Value
    {
        get => _value;
        set
        {
            decimal clamped = Minimum <= Maximum
                ? Math.Clamp(value, Minimum, Maximum)
                : value;

            if (_value == clamped) return;

            _value = clamped;

            // в режиме правки на экран идёт _editText, поэтому его тоже
            // надо обновить — иначе значение поменяется незаметно
            if (_isEditing)
            {
                _editText = _value.ToString($"F{DecimalPlaces}");
                _caretIndex = _editText.Length;
                ResetCaretBlink();
            }

            ValueChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
    }

    public event EventHandler? ValueChanged;

    public Color ButtonColor { get; set; } = new Color(255, 240, 240, 240);
    public Color ButtonHoverColor { get; set; } = new Color(255, 220, 220, 220);
    public Color ArrowColor { get; set; } = Colors.Black;

    public NumericUpDown()
    {
        SetControlDefault(BackgroundProperty, Colors.White);
        Padding = new Thickness(6, 3);
        SetControlDefault(BorderColorProperty, Colors.Black);
        SetControlDefault(BorderWidthProperty, 1f);

        // курсор-текст от TextInputControl тут не к месту: поле в основном
        // управляется кнопками, а редактирование — вторично
        Cursor = CursorKind.Arrow;
    }

    private string Formatted => _value.ToString($"F{DecimalPlaces}");

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

            // текст выровнен вправо, поэтому каретку считаем от правого края
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

        // не распарсилось — молча возвращаем прежнее значение,
        // ронять приложение из-за опечатки не за что
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

        // разделитель уже есть — второй не нужен
        if (isSeparator && (_editText?.Contains(DecimalSeparator) ?? false)) return;

        // точку с клавиатуры приводим к разделителю текущей культуры,
        // иначе decimal.TryParse её не примет
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
                BeginEdit();   // сразу возвращаемся в режим правки
                return true;

            case Key.Escape:
                _editText = Formatted;
                _caretIndex = _editText.Length;
                break;

            case Key.Back when _caretIndex > 0:
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
        // меряем по самому широкому из граничных значений, чтобы поле
        // не прыгало по ширине при переборе
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
