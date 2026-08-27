using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

public class TextBox : UnitControl, ITextElement, IInputElement, IBorderedElement
{
    // ITextElement
    public HorizontalAlign HorizontalAlign { get; set; } = HorizontalAlign.Left;
    public VerticalAlign VerticalAlign { get; set; } = VerticalAlign.Top;

    private float LineHeight => TextMeasurer.Current.MeasureText("Wg").Height;
    private const float CaretWidth = 1f;
    private const int BlinkIntervalMs = 530; // системный дефолт Windows

    private string _text = string.Empty;
    private int _caretIndex;
    private float _scrollOffset;
    private bool _caretVisible;

    private readonly System.Threading.Timer _blinkTimer;

    public string Text
    {
        get => _text;
        set
        {
            _text = value ?? string.Empty;
            _caretIndex = Math.Min(_caretIndex, _text.Length);
            Invalidate();
        }
    }

    public int MaxLength { get; set; } = int.MaxValue;
    public bool IsReadOnly { get; set; }
    public char? PasswordChar { get; set; }

    public Color TextColor { get; set; } = Colors.Black;
    public Color CaretColor { get; set; } = Colors.Black;

    // IBorderedElement
    public Color BorderColor { get; set; } = Colors.Black;
    public float BorderWidth { get; set; } = 1f;

    // IInputElement
    public bool IsFocused { get; set; }
    public bool TabStop { get; set; } = true;
    public uint TabIndex { get; set; }

    public event EventHandler? TextChanged;

    public TextBox()
    {
        Background = Colors.White;
        Padding = new Thickness(4, 2);
        _blinkTimer = new System.Threading.Timer(OnBlink, null, Timeout.Infinite, Timeout.Infinite);
    }

    private string DisplayText =>
        PasswordChar is char pc ? new string(pc, _text.Length) : _text;

    // ===== фокус и мигание каретки =====

    protected override void OnGotFocus()
    {
        _caretVisible = true;
        _blinkTimer.Change(BlinkIntervalMs, BlinkIntervalMs);
    }

    protected override void OnLostFocus()
    {
        _blinkTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _caretVisible = false;
    }

    // выполняется на потоке пула — трогать дерево можно только через Invoke
    private void OnBlink(object? state)
    {
        Form? owner = FindOwner();
        owner?.Invoke(() =>
        {
            _caretVisible = !_caretVisible;
            Invalidate();
        });
    }

    // ===== ввод =====

    protected override void OnTextInput(char c)
    {
        if (IsReadOnly || char.IsControl(c)) return;
        if (_text.Length >= MaxLength) return;

        _text = _text.Insert(_caretIndex, c.ToString());
        _caretIndex++;

        ShowCaretImmediately();
        TextChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool handled = true;

        switch (e.Key)
        {
            case Key.Back when !IsReadOnly && _caretIndex > 0:
                _text = _text.Remove(_caretIndex - 1, 1);
                _caretIndex--;
                TextChanged?.Invoke(this, EventArgs.Empty);
                break;

            case Key.Delete when !IsReadOnly && _caretIndex < _text.Length:
                _text = _text.Remove(_caretIndex, 1);
                TextChanged?.Invoke(this, EventArgs.Empty);
                break;

            case Key.Left:
                _caretIndex = Math.Max(0, _caretIndex - 1);
                break;

            case Key.Right:
                _caretIndex = Math.Min(_text.Length, _caretIndex + 1);
                break;

            case Key.Home:
                _caretIndex = 0;
                break;

            case Key.End:
                _caretIndex = _text.Length;
                break;

            default:
                handled = false;
                break;
        }

        if (handled)
        {
            e.Handled = true;
            ShowCaretImmediately();
            Invalidate();
        }
    }

    protected override void OnClick(MouseClickEventArgs e)
    {
        // ставим каретку в позицию, ближайшую к точке клика
        float localX = e.Location.X - GetAbsolutePosition().X - Padding.Left + _scrollOffset;

        string display = DisplayText;
        int index = display.Length;

        for (int i = 0; i <= display.Length; i++)
        {
            if (TextMeasurer.Current.MeasureTextWidth(display, i) >= localX)
            {
                index = i;
                break;
            }
        }

        _caretIndex = index;
        ShowCaretImmediately();
        Invalidate();
    }

    private void ShowCaretImmediately()
    {
        _caretVisible = true;
        if (IsFocused)
            _blinkTimer.Change(BlinkIntervalMs, BlinkIntervalMs);
    }

    // ===== отрисовка =====

    public override void Draw(Graphics g)
    {
        if (Background.A > 0)
            g.FillRectangle(this.LocalBounds, Background);

        if (BorderWidth > 0)
            g.DrawRectangle(this.LocalBounds, IsFocused ? LightThemeColors.ButtonFill : BorderColor, BorderWidth);

        var content = this.ContentBounds;
        string display = DisplayText;

        float caretX = TextMeasurer.Current.MeasureTextWidth(display, _caretIndex);

        // держим каретку в видимой части поля
        if (caretX - _scrollOffset > content.Width)
            _scrollOffset = caretX - content.Width;
        else if (caretX - _scrollOffset < 0)
            _scrollOffset = caretX;

        // UnitControl'ы SkiaRenderer не клипает — клипаем себя сами,
        // иначе длинный текст вылезет за рамку поля
        g.Save();
        g.ClipRect(content);

        float lineHeight = LineHeight;

        // прямоугольник в одну строку высотой — от него пляшут и текст, и каретка
        float lineY = VerticalAlign switch
        {
            VerticalAlign.Bottom => content.Y + content.Height - lineHeight,
            VerticalAlign.Center => content.Y + (content.Height - lineHeight) / 2f,
            _ => content.Y,   // Top
        };

        if (display.Length > 0)
        {
            var textRect = new Rectangle(
                new Point(content.X - _scrollOffset, lineY),
                new Size(float.MaxValue, lineHeight));

            g.DrawText(display, textRect, TextColor, HorizontalAlign.Left, VerticalAlign.Center);
        }

        if (IsFocused && _caretVisible)
        {
            var caretRect = new Rectangle(
                new Point(content.X + caretX - _scrollOffset, lineY),
                new Size(CaretWidth, lineHeight));

            g.FillRectangle(caretRect, CaretColor);
        }

        g.Restore();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Size probe = TextMeasurer.Current.MeasureText("Wg");
        var content = new Size(120 + Padding.Horizontal, probe.Height + Padding.Vertical + 6);
        return ResolveSize(content, availableSize);
    }
}
