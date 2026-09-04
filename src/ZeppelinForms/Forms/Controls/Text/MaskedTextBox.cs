using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls.Text;

public class MaskedTextBox : TextInputControl
{
    private const float CaretWidth = 1f;

    private MaskDefinition _mask = MaskDefinition.Phone;
    private char[] _buffer;
    private int _caretIndex;

    public MaskedTextBox()
    {
        _buffer = _mask.CreateBuffer();
        _caretIndex = _mask.NextPlaceholder(0);

        Background = Colors.White;
        Padding = new Thickness(6, 3);
        BorderColor = Colors.Black;
        BorderWidth = 1f;
    }

    public MaskDefinition Mask
    {
        get => _mask;
        set
        {
            _mask = value;
            _buffer = value.CreateBuffer();
            _caretIndex = value.NextPlaceholder(0);
            Invalidate();
        }
    }

    /// <summary>Текст вместе с литералами и приглашениями.</summary>
    public string DisplayText => new(_buffer);

    /// <summary>Только введённые символы, без литералов и приглашений.</summary>
    public string RawText
    {
        get
        {
            var result = new System.Text.StringBuilder();

            for (int i = 0; i < _buffer.Length; i++)
                if (_mask.IsPlaceholder(i) && _buffer[i] != _mask.PromptChar)
                    result.Append(_buffer[i]);

            return result.ToString();
        }
    }

    /// <summary>Все обязательные позиции заполнены.</summary>
    public bool IsComplete
    {
        get
        {
            for (int i = 0; i < _buffer.Length; i++)
                if (_mask.IsRequired(i) && _buffer[i] == _mask.PromptChar)
                    return false;

            return true;
        }
    }

    public Color TextColor { get; set; } = Colors.Black;
    public Color PromptColor { get; set; } = new Color(255, 170, 170, 170);
    public Color CaretColor { get; set; } = Colors.Black;

    public event EventHandler? TextChanged;
    public event EventHandler? Accepted;

    public void Clear()
    {
        _buffer = _mask.CreateBuffer();
        _caretIndex = _mask.NextPlaceholder(0);

        TextChanged?.Invoke(this, EventArgs.Empty);
        ResetCaretBlink();
        InvalidateVisual();
    }

    /// <summary>Заполнить маску из строки, пропуская несовпадающие символы.</summary>
    public void SetRawText(string value)
    {
        _buffer = _mask.CreateBuffer();

        int position = _mask.NextPlaceholder(0);

        foreach (char c in value)
        {
            if (position >= _buffer.Length) break;

            if (_mask.Accepts(position, c))
            {
                _buffer[position] = c;
                position = _mask.NextPlaceholder(position + 1);
            }
        }

        _caretIndex = position;

        TextChanged?.Invoke(this, EventArgs.Empty);
        ResetCaretBlink();
        InvalidateVisual();
    }

    protected override void OnTextInput(char c)
    {
        if (char.IsControl(c)) return;

        int position = _mask.IsPlaceholder(_caretIndex)
            ? _caretIndex
            : _mask.NextPlaceholder(_caretIndex);

        if (position >= _buffer.Length) return;

        // символ не подходит под тип позиции — просто игнорируем ввод,
        // без звука и мигания, как делают системные поля
        if (!_mask.Accepts(position, c)) return;

        _buffer[position] = c;
        _caretIndex = _mask.NextPlaceholder(position + 1);

        TextChanged?.Invoke(this, EventArgs.Empty);
        ResetCaretBlink();
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                Accepted?.Invoke(this, EventArgs.Empty);
                break;

            case Key.Back:
                {
                    int position = _mask.IsPlaceholder(_caretIndex) && _buffer[_caretIndex] != _mask.PromptChar
                        ? _caretIndex
                        : _mask.PreviousPlaceholder(_caretIndex);

                    if (position < 0) break;

                    _buffer[position] = _mask.PromptChar;
                    _caretIndex = position;

                    TextChanged?.Invoke(this, EventArgs.Empty);
                    break;
                }

            case Key.Delete:
                if (_mask.IsPlaceholder(_caretIndex))
                {
                    _buffer[_caretIndex] = _mask.PromptChar;
                    TextChanged?.Invoke(this, EventArgs.Empty);
                }
                break;

            case Key.Left:
                {
                    int previous = _mask.PreviousPlaceholder(_caretIndex);
                    if (previous >= 0) _caretIndex = previous;
                    break;
                }

            case Key.Right:
                _caretIndex = _mask.NextPlaceholder(_caretIndex + 1);
                break;

            case Key.Home:
                _caretIndex = _mask.NextPlaceholder(0);
                break;

            case Key.End:
                _caretIndex = LastFilledPlaceholder();
                break;

            case (Key)0x43 when e.Modifiers.HasFlag(KeyModifiers.Control):   // Ctrl+C
                Clipboard.Current.SetText(DisplayText);
                break;

            case (Key)0x56 when e.Modifiers.HasFlag(KeyModifiers.Control):   // Ctrl+V
                if (Clipboard.Current.GetText() is string pasted)
                    SetRawText(pasted);
                break;

            default:
                return;
        }

        e.Handled = true;
        ResetCaretBlink();
        InvalidateVisual();
    }

    private int LastFilledPlaceholder()
    {
        for (int i = _buffer.Length - 1; i >= 0; i--)
            if (_mask.IsPlaceholder(i) && _buffer[i] != _mask.PromptChar)
                return _mask.NextPlaceholder(i + 1);

        return _mask.NextPlaceholder(0);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        float localX = e.Location.X - GetAbsolutePosition().X - Padding.Left;
        string text = DisplayText;

        int index = text.Length;

        for (int i = 0; i <= text.Length; i++)
        {
            if (TextMeasurer.Current.MeasureTextWidth(text, i, EffectiveFont) >= localX)
            {
                index = i;
                break;
            }
        }

        // каретка встаёт только на редактируемые позиции
        _caretIndex = _mask.IsPlaceholder(index) ? index : _mask.NextPlaceholder(index);

        ResetCaretBlink();
        InvalidateVisual();
    }

    protected override void DrawContent(Graphics g)
    {
        Rectangle content = ContentBounds;

        float lineHeight = TextMeasurer.Current.MeasureText("Wg", EffectiveFont).Height;
        float y = content.Y + (content.Height - lineHeight) / 2f;

        // введённое и приглашения рисуем разными цветами, поэтому
        // идём по символам, а не выводим строку целиком
        float x = content.X;

        for (int i = 0; i < _buffer.Length; i++)
        {
            string symbol = _buffer[i].ToString();

            bool isPrompt = _mask.IsPlaceholder(i) && _buffer[i] == _mask.PromptChar;

            g.DrawText(symbol,
                new Rectangle(new Point(x, y), new Size(float.MaxValue, lineHeight)),
                isPrompt ? PromptColor : TextColor, EffectiveFont,
                HorizontalContentAlignment.Left, VerticalContentAlignment.Center);

            x += TextMeasurer.Current.MeasureText(symbol, EffectiveFont).Width;
        }

        if (!IsFocused || !CaretVisible) return;

        float caretX = content.X + TextMeasurer.Current.MeasureTextWidth(
            DisplayText, Math.Min(_caretIndex, _buffer.Length), EffectiveFont);

        g.FillRectangle(
            new Rectangle(new Point(caretX, y), new Size(CaretWidth, lineHeight)),
            CaretColor);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Size textSize = TextMeasurer.Current.MeasureText(DisplayText, EffectiveFont);

        return ResolveSize(
            new Size(
                textSize.Width + Padding.Horizontal + 4,
                textSize.Height + Padding.Vertical + 6),
            availableSize);
    }
}