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
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls.Text;

public partial class TextBox : TextInputControl, ITextElement
{
    /// <summary>A hint in an empty field.</summary>
    public string? Watermark
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            InvalidateVisual();
        }
    }

    public Color WatermarkColor
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            InvalidateVisual();
        }
    } = new Color(255, 160, 160, 160);

    public ValidationState ValidationState { get; private set; } = ValidationState.None;

    public string? ValidationMessage { get; private set; }

    /// <summary>Content check. Returns null if everything is fine.</summary>
    public Func<string, string?>? Validator { get; set; }

    public Color SuccessColor { get; set; } = new Color(255, 0x19, 0x87, 0x54);
    public Color ErrorColor { get; set; } = new Color(255, 0xDC, 0x35, 0x45);

    public event EventHandler? ValidationChanged;

    private const float CaretWidth = 1f;

    private readonly TextDocument _document = new();

    private float _scrollOffset;
    private float _verticalOffset;
    private bool _isDragging;
    private char? _pendingHighSurrogate;

    /// <summary>The document is being written through the Text property —
    /// SetValue, a binding or ClearValue. Each of them raises PropertyChanged
    /// itself, so the document's Changed must not raise it a second time.</summary>
    private bool _writingText;

    // the tooltip as it was before a validation error replaced it with its message
    private string? _toolTipBeforeError;
    private bool _showingErrorToolTip;

    public TextBox()
    {
        SetControlDefault(BackgroundProperty, Colors.White);
        SetControlDefault(PaddingProperty, new(4, 2));
        SetControlDefault(BorderWidthProperty, 1f);

        _document.Changed += (_, _) =>
        {
            // input changes the document directly, bypassing the assignment
            // to Text, so the binding has to be pushed from here. A write through
            // the property notifies on its own path — here it would be a duplicate
            if (!_writingText)
                NotifyBoundValueChanged(TextProperty, _document.Text);

            TextChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        };

        _document.CaretMoved += (_, _) =>
        {
            ResetCaretBlink();     // ← from the base
            InvalidateVisual();
        };
    }

    // ===== public API =====

    public bool IsMultiline
    {
        get => _document.IsMultiline;
        set
        {
            if (_document.IsMultiline == value) return;

            _document.IsMultiline = value;

            // a multi-line field is taller by default
            Invalidate();
        }
    }

    public int MaxLength
    {
        get => _document.MaxLength;
        set => _document.MaxLength = value;
    }

    public bool IsEnterAccepted { get; set; } = true;
    public bool IsTabAccepted { get; set; }
    public bool IsReadOnly { get; set; }

    public char? PasswordChar
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            InvalidateVisual();
        }
    }

    public int SelectionStart => _document.SelectionStart;
    public int SelectionLength => _document.SelectionLength;
    public string SelectedText => _document.SelectedText;

    public int CaretIndex => _document.CaretIndex;

    /// <summary>The field's text. Stored in the document, so the property is external:
    /// the generator must not create a field for it — keyboard input changes
    /// the document directly, and a separate field would drift apart from it.</summary>
    [Styled(Category = "Text", AffectsLayout = true, External = true)]
    public string? Text
    {
        get => _document.Text;
        set => SetValue(TextProperty, value ?? string.Empty);
    }
    private static string? TextDefault => string.Empty;

    /// <summary>A write to the storage, bypassing the ladder of sources. Called
    /// only by the StyledProperty delegate — the setter goes through SetValue,
    /// and assigning Text directly from here would give infinite recursion.</summary>
    private void WriteText(string? value)
    {
        _writingText = true;

        try
        {
            _document.Text = value ?? string.Empty;
        }
        finally
        {
            _writingText = false;
        }
    }

    [Styled(Category = "Text")]
    public partial Color CaretColor { get; set; }
    private static Color CaretColorDefault => Colors.Black;

    [Styled(Category = "Selection")]
    public partial Color SelectionColor { get; set; }
    private static Color SelectionColorDefault => new(255, 173, 214, 255);

    public HorizontalContentAlignment HorizontalContentAlign { get; set; } = HorizontalContentAlignment.Left;
    public VerticalContentAlignment VerticalContentAlign { get; set; } = VerticalContentAlignment.Top;

    public event EventHandler? TextChanged;
    public event EventHandler? Accepted;

    public void SelectAll() => _document.SelectAll();

    /// <summary>Check the content now.</summary>
    public bool Validate()
    {
        if (Validator is null)
        {
            SetValidation(ValidationState.None, null);
            ShowErrorToolTip(null);
            return true;
        }

        string? error = Validator(_document.Text);

        SetValidation(error is null ? ValidationState.Success : ValidationState.Error, error);

        // the error message is shown as a tooltip — there is no separate place for it
        ShowErrorToolTip(error);

        return error is null;
    }

    /// <summary>Show the validation error in the tooltip, or put the user's tooltip
    /// back. Previously the tooltip was simply overwritten — with the message on
    /// an error and with null on success — and a tooltip set by the user was gone
    /// after the first validation.</summary>
    private void ShowErrorToolTip(string? error)
    {
        if (error is not null)
        {
            if (!_showingErrorToolTip)
            {
                _toolTipBeforeError = ToolTip;
                _showingErrorToolTip = true;
            }

            ToolTip = error;
            return;
        }

        if (!_showingErrorToolTip) return;

        ToolTip = _toolTipBeforeError;
        _toolTipBeforeError = null;
        _showingErrorToolTip = false;
    }

    private void SetValidation(ValidationState state, string? message)
    {
        if (ValidationState == state && ValidationMessage == message) return;

        ValidationState = state;
        ValidationMessage = message;

        ValidationChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>The border shows the validation result. SuccessColor and ErrorColor
    /// were declared but used nowhere, so a field with an error looked like any
    /// other. An error beats the focus color: it is what needs attention.</summary>
    protected override Color CurrentBorderColor => ValidationState switch
    {
        ValidationState.Error => ErrorColor,
        ValidationState.Success => SuccessColor,
        _ => base.CurrentBorderColor,
    };

    // ===== display =====

    private float LineHeight => TextMeasurer.Current.MeasureText("Wg", EffectiveFont).Height;

    // the mask is built by visible characters: one emoji — one dot
    private string DisplayText => PasswordChar is char pc && !IsMultiline
        ? new string(pc, _document.Length)
        : _document.Text;

    private string[] DisplayLines => IsMultiline ? _document.Lines : [DisplayText];

    /// <summary>Index in the text → index in the displayed line. They differ under the password mask.</summary>
    private int ToDisplayIndex(int textIndex) =>
        PasswordChar is not null && !IsMultiline
            ? TextElements.Count(_document.Text[..Math.Min(textIndex, _document.Text.Length)])
            : textIndex;

    // ===== focus and blinking =====

    protected override void OnLostFocus()
    {
        base.OnLostFocus();

        // validate when leaving the field, not on every character:
        // otherwise half of a typed address would turn red
        Validate();
    }

    // ===== input =====

    protected override void OnTextInput(char c)
    {
        if (IsReadOnly || char.IsControl(c)) return;

        // an emoji arrives in two messages — the pair is assembled before inserting
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

            case Key.Backspace when !IsReadOnly:
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

            case Key.A when ctrl:
                _document.SelectAll();
                break;

            case Key.C when ctrl:
                if (_document.SelectionLength > 0 && PasswordChar is null)
                    Clipboard.Current.SetText(_document.SelectedText);
                break;

            case Key.X when ctrl && !IsReadOnly:
                if (_document.SelectionLength > 0 && PasswordChar is null)
                {
                    Clipboard.Current.SetText(_document.SelectedText);
                    _document.DeleteSelection();
                }
                break;

            case Key.V when ctrl && !IsReadOnly:
                Paste();
                break;

            case Key.Z when ctrl && !shift && !IsReadOnly:
                _document.Undo();
                break;

            case Key.Y when ctrl && !IsReadOnly:
                _document.Redo();
                break;

            // Ctrl+Shift+Z is redo by the common convention;
            // this branch used to call Undo, the same as plain Ctrl+Z
            case Key.Z when ctrl && shift && !IsReadOnly:
                _document.Redo();
                break;

            default:
                return;   // not our key — don't mark it as handled
        }

        e.Handled = true;
    }

    /// <summary>Insert the clipboard text in place of the selection.</summary>
    /// <remarks>
    /// The body of the Ctrl+V branch had been lost — only a placeholder comment
    /// was left in its place — and pasting did nothing at all.
    ///
    /// A single-line field takes only the first line, as the system edit control
    /// does: a line break has nowhere to go in it, and gluing the lines together
    /// would silently change what was copied. A multi-line field brings line
    /// endings to \n — the only separator the document knows.
    /// </remarks>
    private void Paste()
    {
        if (IsReadOnly) return;
        if (Clipboard.Current.GetText() is not { Length: > 0 } text) return;

        text = text.ReplaceLineEndings("\n");

        if (!IsMultiline)
        {
            int lineBreak = text.IndexOf('\n');

            if (lineBreak >= 0)
                text = text[..lineBreak];
        }

        if (text.Length == 0) return;

        _document.Insert(text);
    }

    // ===== mouse =====

    private int IndexFromPoint(Point location)
    {
        Point abs = GetAbsolutePosition();
        float localX = location.X - abs.X - Padding.Left + _scrollOffset;
        float localY = location.Y - abs.Y - Padding.Top + _verticalOffset;

        string[] lines = DisplayLines;
        int line = IsMultiline ? Math.Clamp((int)(localY / LineHeight), 0, lines.Length - 1) : 0;
        string lineText = lines[line];

        int column = lineText.Length;

        // boundaries of characters are walked rather than chars —
        // otherwise the caret would land in the middle of a surrogate pair
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

    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        if (_isDragging)
            _document.SetCaret(IndexFromPoint(e.Location), extendSelection: true);
    }

    protected override void OnMouseUp(MouseButtonEventArgs args) => _isDragging = false;

    // ===== drawing =====

    protected override void DrawContent(Graphics g)
    {
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

        // a hint instead of the text while the field is empty and not focused
        if (_document.Text.Length == 0 && !IsFocused && !string.IsNullOrEmpty(Watermark))
        {
            g.DrawText(Watermark,
                new Rectangle(new Point(content.X, blockTop), new Size(content.Width, lineHeight)),
                WatermarkColor, EffectiveFont,
                HorizontalContentAlignment.Left, VerticalContentAlignment.Center);
        }

        if (IsFocused && CaretVisible)
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

        // don't scroll below the last line
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
}