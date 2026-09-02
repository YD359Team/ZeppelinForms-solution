using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ZeppelinForms.Core.Text;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls.Text;

public class TextBox : UnitControl, ITextElement, IInputElement, IBorderedElement, IDisposable
{
    /// <summary>Подсказка в пустом поле.</summary>
    public string? Watermark { get; set; }

    public Color WatermarkColor { get; set; } = new Color(255, 160, 160, 160);

    public ValidationState ValidationState { get; private set; } = ValidationState.None;

    public string? ValidationMessage { get; private set; }

    /// <summary>Проверка содержимого. Возвращает null, если всё в порядке.</summary>
    public Func<string, string?>? Validator { get; set; }

    public Color SuccessColor { get; set; } = new Color(255, 0x19, 0x87, 0x54);
    public Color ErrorColor { get; set; } = new Color(255, 0xDC, 0x35, 0x45);

    public event EventHandler? ValidationChanged;

    private const float CaretWidth = 1f;
    private const int BlinkIntervalMs = 530;

    private readonly TextDocument _document = new();
    private readonly System.Threading.Timer _blinkTimer;

    private float _scrollOffset;
    private float _verticalOffset;
    private bool _caretVisible;
    private bool _isDragging;
    private char? _pendingHighSurrogate;
    private bool _disposed;

    public TextBox()
    {
        Background = Colors.White;
        Padding = new Thickness(4, 2);
        this.Cursor = CursorKind.IBeam;

        _document.Changed += (_, _) =>
        {
            TextChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        };

        _document.CaretMoved += (_, _) =>
        {
            ShowCaretImmediately();
            InvalidateVisual();
        };

        _blinkTimer = new System.Threading.Timer(OnBlink, null, Timeout.Infinite, Timeout.Infinite);
    }

    // ===== публичный API =====

    public string? Text
    {
        get => _document.Text;
        set => _document.Text = value ?? string.Empty;
    }

    public bool IsMultiline
    {
        get => _document.IsMultiline;
        set => _document.IsMultiline = value;
    }

    public int MaxLength
    {
        get => _document.MaxLength;
        set => _document.MaxLength = value;
    }

    public bool IsEnterAccepted { get; set; } = true;
    public bool IsTabAccepted { get; set; }
    public bool IsReadOnly { get; set; }
    public char? PasswordChar { get; set; }

    public int SelectionStart => _document.SelectionStart;
    public int SelectionLength => _document.SelectionLength;
    public string SelectedText => _document.SelectedText;

    public int CaretIndex => _document.CaretIndex;

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

    public void SelectAll() => _document.SelectAll();

    /// <summary>Проверить содержимое сейчас.</summary>
    public bool Validate()
    {
        if (Validator is null)
        {
            SetValidation(ValidationState.None, null);
            return true;
        }

        string? error = Validator(_document.Text);

        SetValidation(error is null ? ValidationState.Success : ValidationState.Error, error);

        // сообщение об ошибке показываем подсказкой — отдельного места под него нет
        ToolTip = error;

        return error is null;
    }

    private void SetValidation(ValidationState state, string? message)
    {
        if (ValidationState == state && ValidationMessage == message) return;

        ValidationState = state;
        ValidationMessage = message;

        ValidationChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private Color CurrentBorderColor => ValidationState switch
    {
        ValidationState.Error => ErrorColor,
        ValidationState.Success => SuccessColor,
        _ => IsFocused ? App.Theme.Colors.BorderFocused : BorderColor,
    };

    // ===== отображение =====

    private float LineHeight => TextMeasurer.Current.MeasureText("Wg", EffectiveFont).Height;

    // маска строится по видимым символам: одно эмодзи — одна точка
    private string DisplayText => PasswordChar is char pc && !IsMultiline
        ? new string(pc, _document.Length)
        : _document.Text;

    private string[] DisplayLines => IsMultiline ? _document.Lines : [DisplayText];

    /// <summary>Индекс в тексте → индекс в отображаемой строке. Различаются под маской пароля.</summary>
    private int ToDisplayIndex(int textIndex) =>
        PasswordChar is not null && !IsMultiline
            ? TextElements.Count(_document.Text[..Math.Min(textIndex, _document.Text.Length)])
            : textIndex;

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

        // проверяем при уходе из поля, а не на каждый символ:
        // иначе половина введённого адреса будет краснеть
        Validate();
    }

    private void OnBlink(object? state)
    {
        if (_disposed) return;

        FindOwner()?.Invoke(() =>
        {
            _caretVisible = !_caretVisible;
            InvalidateVisual();
        });
    }

    private void ShowCaretImmediately()
    {
        _caretVisible = true;

        if (IsFocused && !_disposed)
            _blinkTimer.Change(BlinkIntervalMs, BlinkIntervalMs);
    }

    // ===== ввод =====

    protected override void OnTextInput(char c)
    {
        if (IsReadOnly || char.IsControl(c)) return;

        // эмодзи приходит двумя сообщениями — собираем пару перед вставкой
        if (char.IsHighSurrogate(c))
        {
            _pendingHighSurrogate = c;
            return;
        }

        if (_pendingHighSurrogate is char high)
        {
            _pendingHighSurrogate = null;

            if (System.Text.Rune.TryCreate(high, c, out Rune rune))
            {
                _document.Insert(rune.ToString());
                return;
            }
        }

        _document.Insert(c.ToString());
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool shift = e.Modifiers.HasFlag(KeyModifiers.Shift);
        bool ctrl = e.Modifiers.HasFlag(KeyModifiers.Control);

        switch (e.Key)
        {
            case Key.Enter:
                if (IsMultiline && !IsEnterAccepted && !IsReadOnly)
                    _document.Insert("\n");
                else
                    Accepted?.Invoke(this, EventArgs.Empty);
                break;

            case Key.Tab when IsTabAccepted && !IsReadOnly:
                _document.Insert("\t");
                break;

            case Key.Back when !IsReadOnly:
                _document.Backspace();
                break;

            case Key.Delete when !IsReadOnly:
                _document.Delete();
                break;

            case Key.Left when ctrl:
                _document.MoveWordLeft(shift);
                break;

            case Key.Right when ctrl:
                _document.MoveWordRight(shift);
                break;

            case Key.Left:
                _document.MoveLeft(shift);
                break;

            case Key.Right:
                _document.MoveRight(shift);
                break;

            case Key.Up when IsMultiline:
                _document.MoveVertical(-1, shift);
                break;

            case Key.Down when IsMultiline:
                _document.MoveVertical(1, shift);
                break;

            case Key.Home:
                if (IsMultiline) _document.MoveToLineStart(shift);
                else _document.SetCaret(0, shift);
                break;

            case Key.End:
                if (IsMultiline) _document.MoveToLineEnd(shift);
                else _document.SetCaret(_document.Text.Length, shift);
                break;

            case (Key)0x41 when ctrl:   // Ctrl+A
                _document.SelectAll();
                break;

            case (Key)0x43 when ctrl:   // Ctrl+C
                if (_document.SelectionLength > 0 && PasswordChar is null)
                    Clipboard.Current.SetText(_document.SelectedText);
                break;

            case (Key)0x58 when ctrl && !IsReadOnly:   // Ctrl+X
                if (_document.SelectionLength > 0 && PasswordChar is null)
                {
                    Clipboard.Current.SetText(_document.SelectedText);
                    _document.DeleteSelection();
                }
                break;

            case (Key)0x56 when ctrl && !IsReadOnly:   // Ctrl+V
                if (Clipboard.Current.GetText() is string pasted)
                {
                    string clean = IsMultiline
                        ? pasted.Replace("\r\n", "\n").Replace('\r', '\n')
                        : pasted.Replace("\r", "").Replace("\n", " ");

                    _document.Insert(clean);
                }
                break;

            case (Key)0x5A when ctrl && !shift && !IsReadOnly:   // Ctrl+Z
                _document.Undo();
                break;

            case (Key)0x59 when ctrl && !IsReadOnly:            // Ctrl+Y
            case (Key)0x5A when ctrl && shift && !IsReadOnly:   // Ctrl+Shift+Z
                _document.Redo();
                break;

            default:
                return;   // не наша клавиша — не помечаем как обработанную
        }

        e.Handled = true;
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

        int column = lineText.Length;

        // перебираем границы символов, а не char — иначе каретка встанет
        // в середину суррогатной пары
        foreach (int boundary in TextElements.Boundaries(lineText))
        {
            if (TextMeasurer.Current.MeasureTextWidth(lineText, boundary, EffectiveFont) >= localX)
            {
                column = boundary;
                break;
            }
        }

        return IsMultiline ? _document.FromPosition(line, column) : column;
    }

    protected override void OnMouseDown(MouseButtonEventArgs args)
    {
        _document.SetCaret(IndexFromPoint(args.Location));
        _isDragging = true;
    }

    protected override void OnMouseExit(MouseMoveEventArgs args)
    {
        if (_isDragging)
            _document.SetCaret(IndexFromPoint(args.Location), extendSelection: true);
    }

    protected override void OnMouseUp(MouseButtonEventArgs args) => _isDragging = false;

    // ===== отрисовка =====

    public override void Draw(Graphics g)
    {
        if (Background.A > 0)
            g.FillRoundRectangle(this.LocalBounds, CornerRadius, Background);

        if (BorderWidth > 0)
            g.DrawRoundRectangle(LocalBounds, CornerRadius, CurrentBorderColor,
                ValidationState == ValidationState.None ? BorderWidth : Math.Max(BorderWidth, 1.5f));

        var content = this.ContentBounds;
        float lineHeight = LineHeight;
        string[] lines = DisplayLines;

        var (caretLine, caretColumn) = IsMultiline
            ? _document.ToPosition(_document.CaretIndex)
            : (0, ToDisplayIndex(_document.CaretIndex));

        caretLine = Math.Clamp(caretLine, 0, lines.Length - 1);

        float caretX = TextMeasurer.Current.MeasureTextWidth(lines[caretLine], caretColumn, EffectiveFont);
        float caretY = caretLine * lineHeight;

        UpdateScroll(content, caretX, caretY, lineHeight, lines.Length);

        g.Save();
        g.ClipRect(content);

        int selectionStart = ToDisplayIndex(_document.SelectionStart);
        int selectionEnd = ToDisplayIndex(_document.SelectionStart + _document.SelectionLength);
        int lineStartIndex = 0;

        float blockTop = content.Y + (IsMultiline ? 0 : VerticalOffsetForSingleLine(content, lineHeight));

        for (int i = 0; i < lines.Length; i++)
        {
            string lineText = lines[i];
            float y = blockTop + i * lineHeight - _verticalOffset;

            if (_document.SelectionLength > 0)
                DrawSelection(g, lineText, lineStartIndex, selectionStart, selectionEnd, content.X, y, lineHeight);

            if (lineText.Length > 0)
            {
                g.DrawText(lineText,
                    new Rectangle(new Point(content.X - _scrollOffset, y), new Size(float.MaxValue, lineHeight)),
                    TextColor, EffectiveFont,
                    HorizontalContentAlignment.Left, VerticalContentAlignment.Center);
            }

            lineStartIndex += lineText.Length + 1;
        }

        // подсказка вместо текста, пока поле пусто и не в фокусе
        if (_document.Text.Length == 0 && !IsFocused && !string.IsNullOrEmpty(Watermark))
        {
            g.DrawText(Watermark,
                new Rectangle(new Point(content.X, blockTop), new Size(content.Width, lineHeight)),
                WatermarkColor, EffectiveFont,
                HorizontalContentAlignment.Left, VerticalContentAlignment.Center);
        }

        if (IsFocused && _caretVisible)
        {
            g.FillRectangle(
                new Rectangle(
                    new Point(content.X + caretX - _scrollOffset, blockTop + caretY - _verticalOffset),
                    new Size(CaretWidth, lineHeight)),
                CaretColor);
        }

        g.Restore();
    }

    private float VerticalOffsetForSingleLine(Rectangle content, float lineHeight) =>
        VerticalContentAlign switch
        {
            VerticalContentAlignment.Bottom => content.Height - lineHeight,
            VerticalContentAlignment.Center => (content.Height - lineHeight) / 2f,
            _ => 0f,
        };

    private void DrawSelection(
        Graphics g, string lineText, int lineStartIndex,
        int selectionStart, int selectionEnd, float x, float y, float lineHeight)
    {
        int lineEndIndex = lineStartIndex + lineText.Length;

        int from = Math.Max(selectionStart, lineStartIndex) - lineStartIndex;
        int to = Math.Min(selectionEnd, lineEndIndex) - lineStartIndex;

        if (to <= from) return;

        float x1 = TextMeasurer.Current.MeasureTextWidth(lineText, from, EffectiveFont);
        float x2 = TextMeasurer.Current.MeasureTextWidth(lineText, to, EffectiveFont);

        g.FillRectangle(
            new Rectangle(new Point(x + x1 - _scrollOffset, y), new Size(x2 - x1, lineHeight)),
            SelectionColor);
    }

    private void UpdateScroll(Rectangle content, float caretX, float caretY, float lineHeight, int lineCount)
    {
        if (caretX - _scrollOffset > content.Width) _scrollOffset = caretX - content.Width;
        else if (caretX - _scrollOffset < 0) _scrollOffset = caretX;

        if (!IsMultiline)
        {
            _verticalOffset = 0;
            return;
        }

        if (caretY + lineHeight - _verticalOffset > content.Height)
            _verticalOffset = caretY + lineHeight - content.Height;
        else if (caretY - _verticalOffset < 0)
            _verticalOffset = caretY;

        // не отматываем ниже последней строки
        _verticalOffset = Math.Clamp(_verticalOffset, 0, Math.Max(0, lineCount * lineHeight - content.Height));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        float lineHeight = LineHeight;

        float height = IsMultiline
            ? Math.Max(lineHeight * 3, lineHeight * DisplayLines.Length)
            : lineHeight;

        return ResolveSize(
            new Size(120 + Padding.Horizontal, height + Padding.Vertical + 6),
            availableSize);
    }

    // ===== жизненный цикл =====

    protected override void OnDetached() => Dispose();

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _blinkTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _blinkTimer.Dispose();

        GC.SuppressFinalize(this);
    }
}
