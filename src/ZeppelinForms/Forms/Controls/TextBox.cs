using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.Forms.Controls;

public class TextBox : UnitControl, ITextElement, IInputElement, IBorderedElement, IDisposable
{
    private const float CaretWidth = 1f;
    private const int BlinkIntervalMs = 530;

    private string _text = string.Empty;
    private int _caretIndex;
    private int _selectionAnchor;
    private float _scrollOffset;      // горизонтальный
    private float _verticalOffset;    // вертикальный (многострочный режим)
    private bool _caretVisible;
    private bool _isDragging;

    private readonly System.Threading.Timer _blinkTimer;

    public bool IsMultiline { get; set; }
    public bool IsEnterAccepted { get; set; } = true;
    public bool IsTabAccepted { get; set; }

    private float LineHeight => TextMeasurer.Current.MeasureText("Wg", EffectiveFont).Height;

    public string Text
    {
        get => _text;
        set
        {
            _text = value ?? string.Empty;
            _caretIndex = Math.Min(_caretIndex, _text.Length);
            _caretIndex = PreviousTextElementIndex(_text, NextTextElementIndex(_text, _caretIndex));
            _selectionAnchor = _caretIndex;
            Invalidate();
        }
    }

    public int MaxLength { get; set; } = int.MaxValue;
    public bool IsReadOnly { get; set; }
    public char? PasswordChar { get; set; }

    private string DisplayText =>
    PasswordChar is char pc && !IsMultiline
        ? new string(pc, new System.Globalization.StringInfo(_text).LengthInTextElements)
        : _text;

    public int SelectionStart => Math.Min(_selectionAnchor, _caretIndex);
    public int SelectionLength => Math.Abs(_caretIndex - _selectionAnchor);
    public string SelectedText => _text.Substring(SelectionStart, SelectionLength);

    public Color TextColor { get; set; } = Colors.Black;
    public Color CaretColor { get; set; } = Colors.Black;
    public Color SelectionColor { get; set; } = new Color(255, 173, 214, 255);

    public HorizontalContentAlignment HorizontalContentAlign { get; set; } = HorizontalContentAlignment.Left;
    public VerticalContentAlignment VerticalContentAlign { get; set; } = VerticalContentAlignment.Top;

    public Color BorderColor { get; set; } = Colors.Black;
    public float BorderWidth { get; set; } = 1f;

    public bool IsFocused { get; set; }
    public bool TabStop { get; set; } = true;
    public uint TabIndex { get; set; }

    public event EventHandler? TextChanged;
    public event EventHandler? Accepted;

    protected override bool IsKeyActivatable => true;

    public TextBox()
    {
        Background = Colors.White;
        Padding = new Thickness(4, 2);
        _blinkTimer = new System.Threading.Timer(OnBlink, null, Timeout.Infinite, Timeout.Infinite);
    }

    private string[] DisplayLines => DisplayText.Split('\n');

    // ===== перевод между линейным индексом и (строка, колонка) =====

    private (int Line, int Column) IndexToPosition(int index)
    {
        int line = 0, lineStart = 0;

        for (int i = 0; i < index && i < _text.Length; i++)
            if (_text[i] == '\n') { line++; lineStart = i + 1; }

        return (line, index - lineStart);
    }

    private int PositionToIndex(int line, int column)
    {
        string[] lines = DisplayLines;
        line = Math.Clamp(line, 0, lines.Length - 1);

        int index = 0;
        for (int i = 0; i < line; i++)
            index += lines[i].Length + 1;   // +1 на сам '\n'

        return index + Math.Clamp(column, 0, lines[line].Length);
    }

    private static int PreviousTextElementIndex(string text, int index)
    {
        if (index <= 0) return 0;

        var enumerator = System.Globalization.StringInfo.GetTextElementEnumerator(text);
        int previous = 0;

        while (enumerator.MoveNext())
        {
            int current = enumerator.ElementIndex;
            if (current >= index) break;
            previous = current;
        }

        return previous;
    }

    private static int NextTextElementIndex(string text, int index)
    {
        if (index >= text.Length) return text.Length;

        var enumerator = System.Globalization.StringInfo.GetTextElementEnumerator(text);

        while (enumerator.MoveNext())
            if (enumerator.ElementIndex > index)
                return enumerator.ElementIndex;

        return text.Length;
    }

    // ===== фокус и мигание =====

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

    private void OnBlink(object? state) =>
        FindOwner()?.Invoke(() => { _caretVisible = !_caretVisible; Invalidate(); });

    private void ShowCaretImmediately()
    {
        _caretVisible = true;
        if (IsFocused)
            _blinkTimer.Change(BlinkIntervalMs, BlinkIntervalMs);
    }

    // ===== редактирование =====

    private void ClearSelection() => _selectionAnchor = _caretIndex;

    private bool DeleteSelection()
    {
        if (SelectionLength == 0) return false;

        _text = _text.Remove(SelectionStart, SelectionLength);
        _caretIndex = SelectionStart;
        ClearSelection();
        return true;
    }

    private void InsertText(string value)
    {
        if (IsReadOnly) return;

        var info = new System.Globalization.StringInfo(_text);
        var addition = new System.Globalization.StringInfo(value);

        // считаем в символах, а не в char; проверяем ДО удаления выделения,
        // иначе при переполнении текст просто исчезнет
        if (info.LengthInTextElements + addition.LengthInTextElements > MaxLength)
            return;

        DeleteSelection();

        _text = _text.Insert(_caretIndex, value);
        _caretIndex += value.Length;
        ClearSelection();

        ShowCaretImmediately();
        TextChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private char? _pendingHighSurrogate;

    protected override void OnTextInput(char c)
    {
        if (char.IsControl(c)) return;

        // эмодзи приходит двумя сообщениями WM_CHAR — сначала старший
        // суррогат, потом младший; вставляем только собранную пару
        if (char.IsHighSurrogate(c))
        {
            _pendingHighSurrogate = c;
            return;
        }

        if (_pendingHighSurrogate is char high)
        {
            _pendingHighSurrogate = null;

            if (char.IsLowSurrogate(c))
            {
                InsertText(new string([high, c]));
                return;
            }
        }

        InsertText(c.ToString());
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool shift = e.Modifiers.HasFlag(KeyModifiers.Shift);
        bool ctrl = e.Modifiers.HasFlag(KeyModifiers.Control);
        bool handled = true;

        switch (e.Key)
        {
            case Key.Enter:
                // многострочный + разрешён перенос -> вставляем \n;
                // иначе трактуем как "ввод завершён"
                if (IsMultiline && !IsEnterAccepted)
                    InsertText("\n");
                else
                    Accepted?.Invoke(this, EventArgs.Empty);
                break;

            case Key.Tab when IsTabAccepted:
                InsertText("\t");
                break;

            case Key.Back when !IsReadOnly:
                if (!DeleteSelection() && _caretIndex > 0)
                {
                    _text = _text.Remove(_caretIndex - 1, 1);
                    _caretIndex--;
                    ClearSelection();
                }
                TextChanged?.Invoke(this, EventArgs.Empty);
                break;

            case Key.Delete when !IsReadOnly:
                if (!DeleteSelection() && _caretIndex < _text.Length)
                    _text = _text.Remove(_caretIndex, 1);
                TextChanged?.Invoke(this, EventArgs.Empty);
                break;

            case Key.Left:
                _caretIndex = PreviousTextElementIndex(_text, _caretIndex);
                if (!shift) ClearSelection();
                break;

            case Key.Right:
                _caretIndex = NextTextElementIndex(_text, _caretIndex);
                if (!shift) ClearSelection();
                break;

            case Key.Back when !IsReadOnly:
                if (!DeleteSelection() && _caretIndex > 0)
                {
                    int start = PreviousTextElementIndex(_text, _caretIndex);
                    _text = _text.Remove(start, _caretIndex - start);
                    _caretIndex = start;
                    ClearSelection();
                }
                TextChanged?.Invoke(this, EventArgs.Empty);
                break;

            case Key.Delete when !IsReadOnly:
                if (!DeleteSelection() && _caretIndex < _text.Length)
                {
                    int end = NextTextElementIndex(_text, _caretIndex);
                    _text = _text.Remove(_caretIndex, end - _caretIndex);
                }
                TextChanged?.Invoke(this, EventArgs.Empty);
                break;

            case Key.Home:
                {
                    var (line, _) = IndexToPosition(_caretIndex);
                    _caretIndex = IsMultiline ? PositionToIndex(line, 0) : 0;
                    if (!shift) ClearSelection();
                    break;
                }

            case Key.End:
                {
                    var (line, _) = IndexToPosition(_caretIndex);
                    _caretIndex = IsMultiline
                        ? PositionToIndex(line, DisplayLines[line].Length)
                        : _text.Length;
                    if (!shift) ClearSelection();
                    break;
                }

            case (Key)0x41 when ctrl:   // Ctrl+A
                _selectionAnchor = 0;
                _caretIndex = _text.Length;
                break;

            case (Key)0x43 when ctrl:   // Ctrl+C
                if (SelectionLength > 0)
                    Clipboard.Current.SetText(SelectedText);
                break;

            case (Key)0x58 when ctrl:   // Ctrl+X
                if (SelectionLength > 0 && !IsReadOnly)
                {
                    Clipboard.Current.SetText(SelectedText);
                    DeleteSelection();
                    TextChanged?.Invoke(this, EventArgs.Empty);
                }
                break;

            case (Key)0x56 when ctrl:   // Ctrl+V
                if (!IsReadOnly && Clipboard.Current.GetText() is string pasted)
                {
                    string clean = IsMultiline
                        ? pasted.Replace("\r\n", "\n").Replace('\r', '\n')
                        : pasted.Replace("\r", "").Replace("\n", " ");

                    InsertText(clean);
                }
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

    // ===== мышь =====

    private int IndexFromPoint(Point location)
    {
        Point abs = GetAbsolutePosition();
        float localX = location.X - abs.X - Padding.Left + _scrollOffset;
        float localY = location.Y - abs.Y - Padding.Top + _verticalOffset;

        string[] lines = DisplayLines;
        int line = IsMultiline ? Math.Clamp((int)(localY / LineHeight), 0, lines.Length - 1) : 0;
        string lineText = lines[line];

        int col = lineText.Length;

        for (int i = 0; i <= lineText.Length;)
        {
            if (TextMeasurer.Current.MeasureTextWidth(lineText, i, EffectiveFont) >= localX)
            {
                col = i;
                break;
            }

            if (i >= lineText.Length) break;
            i = NextTextElementIndex(lineText, i);
        }

        return PositionToIndex(line, col);
    }

    protected override void OnMouseDown(Point location)
    {
        _caretIndex = IndexFromPoint(location);
        ClearSelection();
        _isDragging = true;

        ShowCaretImmediately();
        Invalidate();
    }

    protected override void OnMouseMove(Point location)
    {
        if (!_isDragging) return;

        _caretIndex = IndexFromPoint(location);   // якорь не трогаем
        Invalidate();
    }

    protected override void OnMouseUp(Point location) => _isDragging = false;

    // ===== отрисовка =====

    public override void Draw(Graphics g)
    {
        if (Background.A > 0)
            g.FillRectangle(this.LocalBounds, Background);

        if (BorderWidth > 0)
            g.DrawRectangle(this.LocalBounds, IsFocused ? LightThemeColors.ButtonFill : BorderColor, BorderWidth);

        var content = this.ContentBounds;
        float lineHeight = LineHeight;
        string[] lines = DisplayLines;

        var (caretLine, caretCol) = IndexToPosition(_caretIndex);
        float caretX = TextMeasurer.Current.MeasureTextWidth(lines[Math.Min(caretLine, lines.Length - 1)], caretCol, EffectiveFont);
        float caretY = caretLine * lineHeight;

        // держим каретку в поле зрения
        if (caretX - _scrollOffset > content.Width) _scrollOffset = caretX - content.Width;
        else if (caretX - _scrollOffset < 0) _scrollOffset = caretX;

        if (IsMultiline)
        {
            if (caretY + lineHeight - _verticalOffset > content.Height)
                _verticalOffset = caretY + lineHeight - content.Height;
            else if (caretY - _verticalOffset < 0)
                _verticalOffset = caretY;
        }

        g.Save();
        g.ClipRect(content);

        int selStart = SelectionStart;
        int selEnd = selStart + SelectionLength;
        int lineStartIndex = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            string lineText = lines[i];
            float y = content.Y + i * lineHeight - _verticalOffset;

            // подсветка выделения в пределах этой строки
            if (SelectionLength > 0)
            {
                int lineEndIndex = lineStartIndex + lineText.Length;
                int from = Math.Max(selStart, lineStartIndex) - lineStartIndex;
                int to = Math.Min(selEnd, lineEndIndex) - lineStartIndex;

                if (to > from)
                {
                    float x1 = TextMeasurer.Current.MeasureTextWidth(lineText, from, EffectiveFont);
                    float x2 = TextMeasurer.Current.MeasureTextWidth(lineText, to, EffectiveFont);

                    g.FillRectangle(
                        new Rectangle(new Point(content.X + x1 - _scrollOffset, y), new Size(x2 - x1, lineHeight)),
                        SelectionColor);
                }
            }

            if (lineText.Length > 0)
            {
                g.DrawText(
                    lineText,
                    new Rectangle(new Point(content.X - _scrollOffset, y), new Size(float.MaxValue, lineHeight)),
                    TextColor, EffectiveFont,
                    HorizontalContentAlignment.Left, VerticalContentAlignment.Center);
            }

            lineStartIndex += lineText.Length + 1;
        }

        if (IsFocused && _caretVisible)
        {
            g.FillRectangle(
                new Rectangle(
                    new Point(content.X + caretX - _scrollOffset, content.Y + caretY - _verticalOffset),
                    new Size(CaretWidth, lineHeight)),
                CaretColor);
        }

        g.Restore();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        float lineHeight = LineHeight;
        float height = IsMultiline
            ? Math.Max(lineHeight * 3, lineHeight * DisplayLines.Length)
            : lineHeight;

        var content = new Size(120 + Padding.Horizontal, height + Padding.Vertical + 6);
        return ResolveSize(content, availableSize);
    }

    public void Dispose()
    {
        _blinkTimer?.Dispose();
    }
}