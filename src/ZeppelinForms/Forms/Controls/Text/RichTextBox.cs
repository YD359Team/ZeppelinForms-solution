using ZeppelinForms.Core.Text;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls.Text;

/// <summary>A multiline field that keeps character formatting (bold, italic, colour, …)
/// alongside the plain text.</summary>
/// <remarks>
/// Editing is plain-text based (<see cref="RichTextDocument.Text"/>); styles live in
/// parallel spans and are shifted on each insert or delete. Drawing expands the
/// visible lines into <see cref="TextRun"/> lists.
/// </remarks>
public partial class RichTextBox : TextInputControl, ITextCompositionTarget
{
    private readonly RichTextDocument _document = new();
    private float _scrollY;
    private bool _dragging;

    // ===== IME composition (ITextCompositionTarget) =====
    private string _composition = string.Empty;
    private int _compositionCursor;
    private bool IsComposing => _composition.Length > 0;

    bool ITextCompositionTarget.AcceptsComposition => !IsReadOnly && IsEnabled;

    void ITextCompositionTarget.SetComposition(string text, int cursor)
    {
        ArgumentNullException.ThrowIfNull(text);

        // composition replaces the selection, as typing does
        if (text.Length > 0 && !IsComposing && SelectionLength > 0)
            DeleteSelectionForComposition();

        _composition = text;
        _compositionCursor = Math.Clamp(cursor, 0, text.Length);
        InvalidateVisual();
    }

    /// <summary>Drop the in-place composition: focus lost, a click, text set from code.</summary>
    private void ClearComposition()
    {
        if (!IsComposing) return;
        _composition = string.Empty;
        _compositionCursor = 0;
        InvalidateVisual();
    }

    private void DeleteSelectionForComposition()
    {
        int pos = SelectionStart;
        int len = SelectionLength;
        if (len <= 0) return;
        _document.Text.DeleteSelection();
        _document.NotifyEdit(pos, len, 0);
    }

    protected override void OnLostFocus()
    {
        base.OnLostFocus();
        ClearComposition();
    }
    public RichTextBox()
    {
        TabStop = true;
        _document.Text.IsMultiline = true;
        _document.Text.Changed += (_, _) =>
        {
            InvalidateVisual();
            TextChanged?.Invoke(this, EventArgs.Empty);
        };
        _document.Text.CaretMoved += (_, _) =>
        {
            EnsureCaretVisible();
            InvalidateVisual();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        };
        _document.Changed += (_, _) => InvalidateVisual();
    }

    /// <summary>The formatted document. Prefer <see cref="Text"/> for plain content.</summary>
    public RichTextDocument Document => _document;

    /// <summary>Plain text of the field. Formatting spans are kept separately on
    /// <see cref="Document"/>.</summary>
    [Styled(Category = "Text", AffectsLayout = true, External = true)]
    public string? Text
    {
        get => _document.Content;
        set => SetValue(TextProperty, value ?? string.Empty);
    }
    private static string? TextDefault => string.Empty;

    private void WriteText(string? value)
    {
        ClearComposition();
        _document.Content = value ?? string.Empty;
    }

    public int CaretIndex => _document.Text.CaretIndex;
    public int SelectionStart => _document.Text.SelectionStart;
    public int SelectionLength => _document.Text.SelectionLength;
    public string SelectedText => _document.Text.SelectedText;

    [Styled(Category = "Text")]
    public partial bool IsReadOnly { get; set; }

    [Styled(Category = "Appearance")]
    public partial Color SelectionBrush { get; set; }
    private static Color SelectionBrushDefault => new(255, 0, 120, 215);

    [Styled(Category = "Appearance")]
    public partial Color CaretBrush { get; set; }
    private static Color CaretBrushDefault => new(255, 0, 0, 0);

    public event EventHandler? TextChanged;
    public event EventHandler? SelectionChanged;

    public override bool AcceptsTextInput => IsEnabled && !IsReadOnly;

    // ===== formatting =====

    public void ApplyStyle(RichTextStyle style) =>
        _document.ApplyStyleToSelection(style);

    public void ToggleBold() => ToggleFlag(s => s with { Bold = !s.Bold });
    public void ToggleItalic() => ToggleFlag(s => s with { Italic = !s.Italic });
    public void ToggleUnderline() => ToggleFlag(s => s with { Underline = !s.Underline });

    public void SetForeground(Color color) =>
        ApplyStyle(_document.GetStyleAt(SelectionStart).WithForeground(color));

    private void ToggleFlag(Func<RichTextStyle, RichTextStyle> map)
    {
        if (SelectionLength == 0) return;
        RichTextStyle current = _document.GetStyleAt(SelectionStart);
        ApplyStyle(map(current));
    }

    public void Select(int start, int length)
    {
        int from = Math.Clamp(start, 0, _document.Text.Text.Length);
        int to = Math.Clamp(start + length, 0, _document.Text.Text.Length);
        _document.Text.SetCaret(from);
        if (to != from) _document.Text.SetCaret(to, extendSelection: true);
    }

    public void SelectAll() => _document.Text.SelectAll();

    // ===== editing =====

    protected override void OnTextInput(char c)
    {
        if (IsReadOnly || char.IsControl(c) && c != '\n' && c != '\r') return;

        if (c == '\r') c = '\n';

        int pos = _document.Text.SelectionStart;
        int removed = _document.Text.SelectionLength;
        _document.Text.Insert(c.ToString());
        _document.NotifyEdit(pos, removed, 1);
        ResetCaretBlink();
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool ctrl = e.Modifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.Modifiers.HasFlag(KeyModifiers.Shift);

        switch (e.Key)
        {
            case Key.Backspace when !IsReadOnly:
                TrackDelete(() => _document.Text.Backspace());
                e.Handled = true;
                break;
            case Key.Delete when !IsReadOnly:
                TrackDelete(() => _document.Text.Delete());
                e.Handled = true;
                break;
            case Key.Left when ctrl:
                _document.Text.MoveWordLeft(shift);
                e.Handled = true;
                break;
            case Key.Right when ctrl:
                _document.Text.MoveWordRight(shift);
                e.Handled = true;
                break;
            case Key.Left:
                _document.Text.MoveLeft(shift);
                e.Handled = true;
                break;
            case Key.Right:
                _document.Text.MoveRight(shift);
                e.Handled = true;
                break;
            case Key.Up:
                _document.Text.MoveVertical(-1, shift);
                e.Handled = true;
                break;
            case Key.Down:
                _document.Text.MoveVertical(1, shift);
                e.Handled = true;
                break;
            case Key.Home when ctrl:
                _document.Text.SetCaret(0, shift);
                e.Handled = true;
                break;
            case Key.End when ctrl:
                _document.Text.SetCaret(_document.Text.Text.Length, shift);
                e.Handled = true;
                break;
            case Key.Home:
                _document.Text.MoveToLineStart(shift);
                e.Handled = true;
                break;
            case Key.End:
                _document.Text.MoveToLineEnd(shift);
                e.Handled = true;
                break;
            case Key.A when ctrl:
                SelectAll();
                e.Handled = true;
                break;
            case Key.C when ctrl:
                Copy();
                e.Handled = true;
                break;
            case Key.X when ctrl && !IsReadOnly:
                Cut();
                e.Handled = true;
                break;
            case Key.V when ctrl && !IsReadOnly:
                Paste();
                e.Handled = true;
                break;
            case Key.Z when ctrl && !IsReadOnly:
                if (_document.Text.Undo()) InvalidateVisual();
                e.Handled = true;
                break;
            case Key.Y when ctrl && !IsReadOnly:
                if (_document.Text.Redo()) InvalidateVisual();
                e.Handled = true;
                break;
            case Key.Enter when !IsReadOnly:
                OnTextInput('\n');
                e.Handled = true;
                break;
            case Key.B when ctrl && !IsReadOnly:
                ToggleBold();
                e.Handled = true;
                break;
            case Key.I when ctrl && !IsReadOnly:
                ToggleItalic();
                e.Handled = true;
                break;
            case Key.U when ctrl && !IsReadOnly:
                ToggleUnderline();
                e.Handled = true;
                break;
        }
    }

    private void TrackDelete(Action edit)
    {
        int pos = _document.Text.SelectionStart;
        int removed = Math.Max(1, _document.Text.SelectionLength);
        if (_document.Text.SelectionLength == 0 && _document.Text.CaretIndex == 0 &&
            // backspace at 0
            false)
        {
            return;
        }

        int before = _document.Text.Text.Length;
        int caretBefore = _document.Text.CaretIndex;
        edit();
        int after = _document.Text.Text.Length;
        int delta = before - after;
        if (delta > 0)
            _document.NotifyEdit(Math.Min(caretBefore, pos), delta, 0);
        ResetCaretBlink();
        InvalidateVisual();
    }

    public void Copy()
    {
        if (SelectionLength > 0)
            Clipboard.Current.SetText(SelectedText);
    }

    public void Cut()
    {
        if (IsReadOnly || SelectionLength == 0) return;
        Copy();
        int pos = SelectionStart;
        int len = SelectionLength;
        _document.Text.DeleteSelection();
        _document.NotifyEdit(pos, len, 0);
        InvalidateVisual();
    }

    public void Paste()
    {
        if (IsReadOnly) return;
        if (Clipboard.Current.GetText() is not { Length: > 0 } text) return;

        int pos = SelectionStart;
        int removed = SelectionLength;
        _document.Text.Insert(text);
        _document.NotifyEdit(pos, removed, text.Length);
        InvalidateVisual();
    }

    // ===== pointer =====
    // Mouse Location is in form coordinates (see TextBox.IndexFromPoint).

    private Point ToLocal(Point absolute)
    {
        Point origin = GetAbsolutePosition();
        return new Point(absolute.X - origin.X, absolute.Y - origin.Y);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (e.Button != MouseButton.Left) return;
        Focus();
        ClearComposition();
        int index = IndexFromPoint(e.Location);
        _document.Text.SetCaret(index, extendSelection: e.Modifiers.HasFlag(KeyModifiers.Shift));
        _dragging = true;
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        if (!_dragging) return;
        _document.Text.SetCaret(IndexFromPoint(e.Location), extendSelection: true);
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (e.Button == MouseButton.Left)
            _dragging = false;
    }

    private int IndexFromPoint(Point absolute)
    {
        Point local = ToLocal(absolute);
        Font font = EffectiveFont;
        float lineHeight = TextMeasurer.LineHeight(font);
        Rectangle content = ContentBounds;

        float y = local.Y + _scrollY - content.Y;
        int line = Math.Max(0, (int)(y / lineHeight));
        string[] lines = _document.Text.Lines;
        if (lines.Length == 0) return 0;
        line = Math.Min(line, lines.Length - 1);

        int lineStart = _document.Text.FromPosition(line, 0);
        string lineText = lines[line];
        float x = local.X - content.X;
        if (x <= 0 || lineText.Length == 0)
            return lineStart;

        TextLineLayout layout = TextMeasurer.LayOutLine(lineText, font, Core.Text.FlowDirection.LeftToRight);
        int column = layout.IndexAt(x);
        return Math.Min(lineStart + column, _document.Text.Text.Length);
    }

    // ===== drawing =====

    protected override void DrawContent(Graphics g)
    {
        Rectangle content = ContentBounds;
        g.Save();
        g.ClipRect(content);

        Font font = EffectiveFont;
        Color color = TextColor;
        float lineHeight = TextMeasurer.LineHeight(font);
        string[] lines = _document.Text.Lines;

        float y = content.Y - _scrollY;

        for (int line = 0; line < lines.Length; line++)
        {
            if (y + lineHeight < content.Y)
            {
                y += lineHeight;
                continue;
            }
            if (y > content.Bottom) break;

            int lineStart = _document.Text.FromPosition(line, 0);
            int lineLen = lines[line].Length;

            DrawSelection(g, lineStart, lineLen, content.X, y, lineHeight, font);

            List<TextRun> runs = _document.BuildRuns(lineStart, lineLen, font, color);
            if (runs.Count > 0)
            {
                g.DrawRuns(runs, new Rectangle(content.X, y, content.Width, lineHeight),
                    font, color, HorizontalContentAlignment.Left, VerticalContentAlignment.Top);
            }
            else if (lineLen > 0)
            {
                g.DrawText(lines[line], new Point(content.X, y), color, font);
            }

            y += lineHeight;
        }

        if (IsFocused && CaretVisible && SelectionLength == 0)
            DrawCaret(g, content, font, lineHeight);

        g.Restore();
    }

    private void DrawSelection(Graphics g, int lineStart, int lineLen, float x, float y, float lineHeight, Font font)
    {
        int selStart = SelectionStart;
        int selEnd = selStart + SelectionLength;
        if (SelectionLength == 0) return;

        int from = Math.Max(selStart, lineStart);
        int to = Math.Min(selEnd, lineStart + lineLen);
        if (from >= to) return;

        float x0 = x + MeasureRange(lineStart, from - lineStart, font);
        float x1 = x + MeasureRange(lineStart, to - lineStart, font);
        g.FillRectangle(new Rectangle(x0, y, Math.Max(2, x1 - x0), lineHeight), SelectionBrush);
    }

    private float MeasureRange(int start, int length, Font font)
    {
        if (length <= 0) return 0;
        string slice = _document.Text.Text.Substring(start, Math.Min(length, _document.Text.Text.Length - start));
        return TextMeasurer.Current.MeasureText(slice, font).Width;
    }

    private void DrawCaret(Graphics g, Rectangle content, Font font, float lineHeight)
    {
        var (line, col) = _document.Text.ToPosition(CaretIndex);
        float y = content.Y - _scrollY + line * lineHeight;
        int lineStart = _document.Text.FromPosition(line, 0);
        float x = content.X + MeasureRange(lineStart, col, font);

        if (IsComposing)
        {
            // show the uncommitted text at the caret, underlined
            float compW = TextMeasurer.Current.MeasureText(_composition, font).Width;
            g.DrawText(_composition, new Point(x, y), TextColor, font);
            g.DrawRectangle(new Rectangle(x, y + lineHeight - 2, Math.Max(2, compW), 1), TextColor, 1);
            x += TextMeasurer.Current.MeasureText(
                _composition[..Math.Min(_compositionCursor, _composition.Length)], font).Width;
        }

        g.FillRectangle(new Rectangle(x, y, 1, lineHeight), CaretBrush.A > 0 ? CaretBrush : TextColor);

        if (IsFocused)
            ReportTextInputArea(new Rectangle(x, y, 1, lineHeight));
    }

    private void EnsureCaretVisible()
    {
        float lineHeight = TextMeasurer.LineHeight(EffectiveFont);
        var (line, _) = _document.Text.ToPosition(CaretIndex);
        float caretY = line * lineHeight;
        float view = Math.Max(0, ContentBounds.Height);

        if (caretY < _scrollY)
            _scrollY = caretY;
        else if (caretY + lineHeight > _scrollY + view)
            _scrollY = caretY + lineHeight - view;

        _scrollY = Math.Max(0, _scrollY);
    }

    private new Rectangle ContentBounds =>
        new(Padding.Left, Padding.Top,
            Math.Max(0, LocalBounds.Width - Padding.Horizontal),
            Math.Max(0, LocalBounds.Height - Padding.Vertical));

    protected override Size MeasureOverride(Size availableSize)
    {
        float lineHeight = TextMeasurer.LineHeight(EffectiveFont);
        int lines = Math.Max(1, _document.Text.Lines.Length);
        return new Size(
            Math.Min(availableSize.Width, 200),
            Math.Min(availableSize.Height, lines * lineHeight + Padding.Vertical));
    }
}
