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

    /// <summary>Text entered by the user, cased by TextTransform — like
    /// CharacterCasing in WinForms, the stored text itself changes.</summary>
    /// <remarks>
    /// Only when TextTransform is set on this field itself. It is inherited, and
    /// a panel with upper-case headings must not start rewriting what is typed
    /// into the fields inside it.
    /// </remarks>
    private string CaseInput(string text) =>
        IsLocal(TextTransformProperty) || IsBound(TextTransformProperty)
            ? ApplyTextTransform(text)
            : text;

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

            if (!_writingText)
                OnTextEdited();

            InvalidateVisual();
        };

        _document.CaretMoved += (_, _) =>
        {
            // moving along the rows keeps the x it started from; anything else forgets it
            if (!_movingByRows) _desiredX = null;

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

    /// <summary>A multi-line field breaks long lines at the field's width — between
    /// words, inside a word only when the word alone is wider — instead of scrolling
    /// sideways. The text itself doesn't change: the breaks are only shown.</summary>
    /// <remarks>Up and Down then move by the rows on screen, Home and End go to the
    /// ends of the row, as in every editor that wraps.</remarks>
    public bool WordWrap
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // the number of rows, and with it the desired height, changes
            Invalidate();
        }
    }

    /// <summary>Show spaces as dots, tabs as arrows and line ends as ¶ — what text
    /// editors show to tell a space from a tab and spot the trailing ones. Not under
    /// a password mask.</summary>
    public bool ShowWhitespace
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            InvalidateVisual();
        }
    }

    /// <summary>The marks of <see cref="ShowWhitespace"/>: faint, so the text stays the text.</summary>
    [Styled(Category = "Text")]
    public partial Color WhitespaceColor { get; set; }
    private static Color WhitespaceColorDefault => new(255, 175, 175, 175);

    public int MaxLength
    {
        get => _document.MaxLength;
        set => _document.MaxLength = value;
    }

    public bool IsEnterAccepted { get; set; } = true;
    public bool IsTabAccepted { get; set; }

    /// <summary>The text can be selected and copied but not changed: the
    /// <c>:read-only</c> pseudo-class.</summary>
    public bool IsReadOnly
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            SetPseudoClass(PseudoClass.ReadOnly, value);
        }
    }

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

    // ===== icons =====

    /// <summary>An icon where the text starts — a magnifier in a search field.</summary>
    public IconSource? LeadingIcon
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // the text area narrows or widens
            Invalidate();
        }
    }

    /// <summary>An icon where the text ends — clear, reveal the password, open a list.
    /// With <see cref="TrailingIconClick"/> handled it is a button.</summary>
    public IconSource? TrailingIcon
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Invalidate();
        }
    }

    public float IconSize
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Invalidate();
        }
    } = 16f;

    /// <summary>Between an icon and the text.</summary>
    public float IconGap
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Invalidate();
        }
    } = 6f;

    /// <summary>The color path icons are drawn in; pictures keep their own.</summary>
    [Styled(Category = "Icons")]
    public partial Color IconColor { get; set; }
    private static Color IconColorDefault => new(255, 110, 110, 110);

    /// <summary>A click on <see cref="LeadingIcon"/>. While nobody handles it, the icon
    /// is a picture: a press on it puts the caret at the start, as on the text.</summary>
    public event EventHandler? LeadingIconClick;

    /// <summary>A click on <see cref="TrailingIcon"/> — say, clearing the field.</summary>
    public event EventHandler? TrailingIconClick;

    public void SelectAll() => _document.SelectAll();

    /// <summary>The text changed by editing — typing, deleting, pasting, undo — not by
    /// an assignment to <see cref="Text"/> or a binding. After <see cref="TextChanged"/>.</summary>
    protected virtual void OnTextEdited() { }

    /// <summary>Select <paramref name="length"/> characters from <paramref name="start"/>,
    /// the caret at the end of them; a length of 0 just puts the caret there.</summary>
    public void Select(int start, int length)
    {
        int from = Math.Clamp(start, 0, _document.Text.Length);
        int to = Math.Clamp(start + length, 0, _document.Text.Length);

        _document.SetCaret(from);
        if (to != from) _document.SetCaret(to, extendSelection: true);
    }

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

        SetPseudoClass(PseudoClass.Invalid, state == ValidationState.Error);
        SetPseudoClass(PseudoClass.Valid, state == ValidationState.Success);

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

    /// <summary>The underline follows the same rule as the border: under an
    /// accent underline the red border of an error would read as two answers
    /// at once. Only where a theme draws an underline at all.</summary>
    protected override Color CurrentUnderlineColor
    {
        get
        {
            Color underline = base.CurrentUnderlineColor;

            if (underline.A == 0) return underline;

            return ValidationState switch
            {
                ValidationState.Error => ErrorColor,
                ValidationState.Success => SuccessColor,
                _ => underline,
            };
        }
    }

    // ===== geometry =====

    private float LeadingWidth => LeadingIcon is null ? 0f : IconSize + IconGap;

    private float TrailingWidth => TrailingIcon is null ? 0f : IconSize + IconGap;

    /// <summary>Where the text goes: the content less the icons. Everything that
    /// places text, the caret or the selection works in it.</summary>
    private Rectangle TextBounds
    {
        get
        {
            Rectangle content = this.ContentBounds;

            return new Rectangle(
                new Point(content.X + LeadingWidth, content.Y),
                new Size(Math.Max(0, content.Width - LeadingWidth - TrailingWidth), content.Height));
        }
    }

    /// <summary>An icon's square: at its edge of the content, level with the single
    /// line, or with the first line of a multi-line field.</summary>
    private Rectangle IconRect(bool leading)
    {
        Rectangle content = this.ContentBounds;
        float lineHeight = LineHeight;

        float top = IsMultiline
            ? content.Y + (lineHeight - IconSize) / 2f
            : content.Y + VerticalOffsetForSingleLine(content, lineHeight) + (lineHeight - IconSize) / 2f;

        float left = leading ? content.X : content.X + content.Width - IconSize;

        return new Rectangle(new Point(left, top), new Size(IconSize, IconSize));
    }

    private enum IconPart : byte { None, Leading, Trailing }

    /// <summary>The clickable icon under a point of the form: one with a handler.</summary>
    private IconPart ClickableIconAt(Point location)
    {
        Point abs = GetAbsolutePosition();
        var local = new Point(location.X - abs.X, location.Y - abs.Y);

        // the whole height of the content and the gap beside the icon: the square
        // itself is small to aim at
        Rectangle content = this.ContentBounds;
        bool inHeight = local.Y >= content.Y && local.Y <= content.Y + content.Height;

        if (!inHeight) return IconPart.None;

        if (LeadingIcon is not null && LeadingIconClick is not null &&
            local.X >= content.X && local.X <= content.X + LeadingWidth)
            return IconPart.Leading;

        if (TrailingIcon is not null && TrailingIconClick is not null &&
            local.X >= content.X + content.Width - TrailingWidth && local.X <= content.X + content.Width)
            return IconPart.Trailing;

        return IconPart.None;
    }

    /// <summary>The icon the press landed on; the click is raised if the release
    /// lands on it too.</summary>
    private IconPart _pressedIcon;

    // ===== display =====

    private float LineHeight => TextMeasurer.Current.MeasureText("Wg", EffectiveFont).Height;

    // the mask is built by visible characters: one emoji — one dot
    private string DisplayText => PasswordChar is char pc && !IsMultiline
        ? new string(pc, _document.Length)
        : _document.Text;

    /// <summary>Index in the text → index in the displayed line. They differ under the password mask.</summary>
    private int ToDisplayIndex(int textIndex) =>
        PasswordChar is not null && !IsMultiline
            ? TextElements.Count(_document.Text[..Math.Min(textIndex, _document.Text.Length)])
            : textIndex;

    // ===== rows =====

    /// <summary>A row on screen: a whole line of the text, or a piece of one under
    /// <see cref="WordWrap"/>. <see cref="Start"/> is the index of its first character in
    /// the text; the rows of a line follow each other without gaps, and spaces at a
    /// break stay at the end of the row before it.</summary>
    /// <param name="EndsLine">The last row of its line: a '\n' or the end of the text follows.</param>
    private readonly record struct TextRow(int Start, string Text, bool EndsLine);

    private IReadOnlyList<TextRow>? _rows;
    private string? _rowsText;
    private float _rowsWidth;
    private Font? _rowsFont;

    /// <summary>The rows at a width. Kept until the text, the width or the font changes:
    /// drawing, hit testing and the arrow keys all ask, often several times a frame.</summary>
    private IReadOnlyList<TextRow> Rows(float width)
    {
        string text = IsMultiline ? _document.Text : DisplayText;
        bool wrap = WordWrap && IsMultiline && width > 0 && float.IsFinite(width);
        float key = wrap ? width : float.PositiveInfinity;
        Font font = EffectiveFont;

        if (_rows is not null && _rowsWidth == key && Equals(_rowsFont, font) && string.Equals(_rowsText, text, StringComparison.Ordinal))
            return _rows;

        var rows = new List<TextRow>();

        if (!IsMultiline)
        {
            rows.Add(new TextRow(0, text, EndsLine: true));
        }
        else
        {
            int lineStart = 0;

            foreach (string line in text.Split('\n'))
            {
                if (wrap && line.Length > 0) Wrap(line, lineStart, width, font, rows);
                else rows.Add(new TextRow(lineStart, line, EndsLine: true));

                lineStart += line.Length + 1;
            }
        }

        _rows = rows;
        _rowsText = text;
        _rowsWidth = key;
        _rowsFont = font;

        return rows;
    }

    private static bool IsSpace(char c) => c is ' ' or '\t';

    /// <summary>Break one line of the text into rows no wider than <paramref name="width"/>.</summary>
    private static void Wrap(string line, int offset, float width, Font font, List<TextRow> rows)
    {
        ITextMeasurer measurer = TextMeasurer.Current;
        int start = 0;

        while (start < line.Length)
        {
            string rest = line[start..];

            if (measurer.MeasureTextWidth(rest, rest.Length, font) <= width)
            {
                rows.Add(new TextRow(offset + start, rest, EndsLine: true));
                return;
            }

            // the longest piece that fits, by a binary search over the character
            // boundaries: widths only grow, and measuring every prefix would be quadratic
            List<int> bounds = [.. TextElements.Boundaries(rest)];

            int fits = 0;
            int low = 1, high = bounds.Count - 1;

            while (low <= high)
            {
                int middle = (low + high) / 2;

                if (measurer.MeasureTextWidth(rest, bounds[middle], font) <= width)
                {
                    fits = middle;
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            // at least one character a row, however narrow the field
            int end = bounds[Math.Max(1, fits)];

            // break after the last space of the piece, so words stay whole; a word
            // wider than the row is broken where it overflows
            if (end < rest.Length && !IsSpace(rest[end]))
            {
                int space = rest.LastIndexOfAny([' ', '\t'], end - 1, end);

                if (space >= 0) end = space + 1;
            }

            // spaces at the break hang at the end of the row, as in every editor
            while (end < rest.Length && IsSpace(rest[end])) end++;

            rows.Add(new TextRow(offset + start, rest[..end], EndsLine: end >= rest.Length));
            start += end;
        }
    }

    /// <summary>The row an index of the text is shown in, and its column there. An index
    /// at a soft break belongs to the row that begins with it.</summary>
    private static (int Row, int Column) RowOf(IReadOnlyList<TextRow> rows, int index)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            TextRow row = rows[i];
            int end = row.Start + row.Text.Length;

            if (index < end || (index == end && (row.EndsLine || i == rows.Count - 1)))
                return (i, Math.Max(0, index - row.Start));
        }

        return (rows.Count - 1, rows[^1].Text.Length);
    }

    /// <summary>The column of a row nearest to <paramref name="x"/>, from the row's left edge.</summary>
    private int ColumnAt(TextRow row, float x)
    {
        string text = row.Text;
        int column = text.Length;

        // boundaries of characters are walked rather than chars —
        // otherwise the caret would land in the middle of a surrogate pair
        foreach (int boundary in TextElements.Boundaries(text))
        {
            if (TextMeasurer.Current.MeasureTextWidth(text, boundary, EffectiveFont) >= x)
            {
                column = boundary;
                break;
            }
        }

        // past the end of a soft-wrapped row the index would be the next row's start,
        // and the caret would jump down there: it stays before the row's last character
        if (!row.EndsLine && column == text.Length && text.Length > 0)
            column = TextElements.Previous(text, text.Length);

        return column;
    }

    // ===== moving along the rows =====

    /// <summary>The x the Up and Down keys aim at: kept across a run of them, so
    /// passing a short row doesn't pull the caret to the left for good.</summary>
    private float? _desiredX;
    private bool _movingByRows;

    private void MoveByRows(int delta, bool extend)
    {
        IReadOnlyList<TextRow> rows = Rows(TextBounds.Width);
        var (row, column) = RowOf(rows, _document.CaretIndex);

        int target = row + delta;

        if (target < 0 || target >= rows.Count) return;

        _desiredX ??= TextMeasurer.Current.MeasureTextWidth(rows[row].Text, column, EffectiveFont);

        SetCaretByRows(rows[target].Start + ColumnAt(rows[target], _desiredX.Value), extend);
    }

    /// <summary>Home and End under word wrap: the ends of the row on screen. The end
    /// of a soft-wrapped row is before its hanging spaces.</summary>
    private void MoveToRowEdge(bool end, bool extend)
    {
        IReadOnlyList<TextRow> rows = Rows(TextBounds.Width);
        TextRow row = rows[RowOf(rows, _document.CaretIndex).Row];

        if (!end)
        {
            _document.SetCaret(row.Start, extend);
            return;
        }

        int column = row.EndsLine ? row.Text.Length : row.Text.TrimEnd(' ', '\t').Length;

        if (!row.EndsLine && column == row.Text.Length && column > 0)
            column = TextElements.Previous(row.Text, column);

        _document.SetCaret(row.Start + column, extend);
    }

    private void SetCaretByRows(int index, bool extend)
    {
        _movingByRows = true;

        try
        {
            _document.SetCaret(index, extend, keepDesiredColumn: true);
        }
        finally
        {
            _movingByRows = false;
        }
    }

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
                _document.Insert(CaseInput(rune.ToString()));
                return;
            }
        }

        _document.Insert(CaseInput(c.ToString()));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool shift = e.Modifiers.HasFlag(KeyModifiers.Shift);
        bool ctrl = e.Modifiers.HasFlag(KeyModifiers.Control);

        switch (e.Key)
        {
            case Key.Enter:
                if (IsMultiline && !IsEnterAccepted && !IsReadOnly)
                {
                    _document.Insert("\n");
                    break;
                }

                // nobody listens to Accepted on a single-line field: Enter is left
                // unhandled and goes on to the form's default button
                if (Accepted is null && !IsMultiline)
                    return;

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

            case Key.Up when IsMultiline && WordWrap:
                MoveByRows(-1, shift);
                break;

            case Key.Down when IsMultiline && WordWrap:
                MoveByRows(1, shift);
                break;

            case Key.Up when IsMultiline:
                _document.MoveVertical(-1, shift);
                break;

            case Key.Down when IsMultiline:
                _document.MoveVertical(1, shift);
                break;

            case Key.Home when ctrl && IsMultiline:
                _document.SetCaret(0, shift);
                break;

            case Key.End when ctrl && IsMultiline:
                _document.SetCaret(_document.Text.Length, shift);
                break;

            case Key.Home:
                if (IsMultiline && WordWrap) MoveToRowEdge(end: false, shift);
                else if (IsMultiline) _document.MoveToLineStart(shift);
                else _document.SetCaret(0, shift);
                break;

            case Key.End:
                if (IsMultiline && WordWrap) MoveToRowEdge(end: true, shift);
                else if (IsMultiline) _document.MoveToLineEnd(shift);
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

        _document.Insert(CaseInput(text));
    }

    // ===== mouse =====

    private int IndexFromPoint(Point location)
    {
        Point abs = GetAbsolutePosition();
        Rectangle text = TextBounds;

        float localX = location.X - abs.X - text.X + _scrollOffset;
        float localY = location.Y - abs.Y - text.Y + _verticalOffset;

        IReadOnlyList<TextRow> rows = Rows(text.Width);

        if (!IsMultiline) return ColumnAt(rows[0], localX);

        TextRow row = rows[Math.Clamp((int)(localY / LineHeight), 0, rows.Count - 1)];

        return row.Start + ColumnAt(row, localX);
    }

    protected override void OnMouseDown(MouseButtonEventArgs args)
    {
        // a press on an icon button is not a press on the text: the caret stays
        _pressedIcon = args.Button == MouseButton.Left ? ClickableIconAt(args.Location) : IconPart.None;

        if (_pressedIcon != IconPart.None) return;

        _document.SetCaret(IndexFromPoint(args.Location));
        _isDragging = true;
    }

    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        if (_isDragging)
        {
            _document.SetCaret(IndexFromPoint(e.Location), extendSelection: true);
            return;
        }

        // a hand over an icon that does something, the I-beam over the text
        Cursor = ClickableIconAt(e.Location) != IconPart.None ? CursorKind.Hand : CursorKind.IBeam;
    }

    protected override void OnMouseUp(MouseButtonEventArgs args)
    {
        _isDragging = false;

        IconPart pressed = _pressedIcon;
        _pressedIcon = IconPart.None;

        if (pressed == IconPart.None || ClickableIconAt(args.Location) != pressed) return;

        if (pressed == IconPart.Leading) LeadingIconClick?.Invoke(this, EventArgs.Empty);
        else TrailingIconClick?.Invoke(this, EventArgs.Empty);
    }

    // ===== drawing =====

    protected override void DrawContent(Graphics g)
    {
        LeadingIcon?.Draw(g, IconRect(leading: true), IconColor);
        TrailingIcon?.Draw(g, IconRect(leading: false), IconColor);

        var content = TextBounds;
        float lineHeight = LineHeight;
        IReadOnlyList<TextRow> rows = Rows(content.Width);

        var (caretRow, caretColumn) = IsMultiline
            ? RowOf(rows, _document.CaretIndex)
            : (0, ToDisplayIndex(_document.CaretIndex));

        float caretX = TextMeasurer.Current.MeasureTextWidth(rows[caretRow].Text, caretColumn, EffectiveFont);
        float caretY = caretRow * lineHeight;

        UpdateScroll(content, caretX, caretY, lineHeight, rows.Count);

        g.Save();
        g.ClipRect(content);

        int selectionStart = ToDisplayIndex(_document.SelectionStart);
        int selectionEnd = ToDisplayIndex(_document.SelectionStart + _document.SelectionLength);

        float blockTop = content.Y + (IsMultiline ? 0 : VerticalOffsetForSingleLine(content, lineHeight));
        bool showWhitespace = ShowWhitespace && PasswordChar is null;

        for (int i = 0; i < rows.Count; i++)
        {
            TextRow row = rows[i];
            float y = blockTop + i * lineHeight - _verticalOffset;

            // rows scrolled out of the field are not drawn: a long text used to be
            // drawn whole on every frame, the clip only hiding it
            if (y + lineHeight < content.Y || y > content.Y + content.Height) continue;

            if (_document.SelectionLength > 0)
                DrawSelection(g, row.Text, row.Start, selectionStart, selectionEnd, content.X, y, lineHeight);

            if (row.Text.Length > 0)
            {
                g.DrawText(row.Text,
                    new Rectangle(new Point(content.X - _scrollOffset, y), new Size(float.MaxValue, lineHeight)),
                    TextColor, EffectiveFont,
                    HorizontalContentAlignment.Left, VerticalContentAlignment.Center);
            }

            if (showWhitespace)
                DrawWhitespace(g, row, content.X - _scrollOffset, y, lineHeight);
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

    /// <summary>Dots for spaces, arrows for tabs, ¶ where a line of the text ends.</summary>
    private void DrawWhitespace(Graphics g, TextRow row, float x, float y, float lineHeight)
    {
        ITextMeasurer measurer = TextMeasurer.Current;
        Font font = EffectiveFont;
        Color color = WhitespaceColor;
        string text = row.Text;
        float middle = y + lineHeight / 2f;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (!IsSpace(c)) continue;

            float left = x + measurer.MeasureTextWidth(text, i, font);
            float right = x + measurer.MeasureTextWidth(text, i + 1, font);

            if (c == ' ')
            {
                float radius = Math.Max(1f, font.Size / 12f);
                float center = (left + right) / 2f;

                g.FillEllipse(
                    new Rectangle(new Point(center - radius, middle - radius), new Size(radius * 2f, radius * 2f)),
                    color);
            }
            else
            {
                float head = Math.Max(2f, font.Size / 5f);
                float tip = Math.Max(left + head, right - 2f);

                g.DrawLine(new Point(left + 2f, middle), new Point(tip, middle), color, 1f);

                ReadOnlySpan<Point> arrow =
                [
                    new(tip - head, middle - head),
                    new(tip, middle),
                    new(tip - head, middle + head),
                ];

                g.DrawPolyline(arrow, color, 1f);
            }
        }

        // the end of a line of the text, not of a row the wrap made, and not after
        // the last line: there is no line break there
        if (IsMultiline && row.EndsLine && row.Start + text.Length < _document.Text.Length)
        {
            float end = x + measurer.MeasureTextWidth(text, text.Length, font);

            g.DrawText("¶",
                new Rectangle(new Point(end + 1f, y), new Size(float.MaxValue, lineHeight)),
                color, font,
                HorizontalContentAlignment.Left, VerticalContentAlignment.Center);
        }
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
        // wrapped rows fit the width: nothing to scroll sideways to
        if (IsMultiline && WordWrap) _scrollOffset = 0;
        else if (caretX - _scrollOffset > content.Width) _scrollOffset = caretX - content.Width;
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

        // wrapped, the rows are counted at the width the field is offered
        float wrapWidth = availableSize.Width - Padding.Horizontal - LeadingWidth - TrailingWidth;

        float height = IsMultiline
            ? Math.Max(lineHeight * 3, lineHeight * Rows(WordWrap ? wrapWidth : float.PositiveInfinity).Count)
            : lineHeight;

        // an icon taller than the line makes the field taller, not one without icons
        float iconHeight = LeadingIcon is null && TrailingIcon is null ? 0f : IconSize;

        return ResolveSize(
            new Size(120 + LeadingWidth + TrailingWidth + Padding.Horizontal, Math.Max(height, iconHeight) + Padding.Vertical + 6),
            availableSize);
    }
}