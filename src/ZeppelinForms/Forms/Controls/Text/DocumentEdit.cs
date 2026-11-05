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

/// <summary>A paginated rich-text document editor (word-processor layout).</summary>
/// <remarks>
/// Content is a <see cref="RichTextDocument"/>. Text is laid out into
/// <see cref="DocumentPage"/>s according to <see cref="PageSettings"/>. Printing
/// raises <see cref="PrintRequested"/> with a snapshot the host can send to a
/// platform printer; ZF itself does not talk to the spooler.
/// </remarks>
public partial class DocumentEdit : TextInputControl, ITextCompositionTarget
{
    private readonly RichTextDocument _document = new();
    private readonly List<DocumentPage> _pages = [];
    private float _scrollY;
    private bool _dragging;
    private bool _layoutDirty = true;

    public DocumentEdit()
    {
        TabStop = true;
        SetControlDefault(BackgroundProperty, new Color(255, 200, 200, 200));
        SetControlDefault(PaddingProperty, new Thickness(16));

        _document.Text.IsMultiline = true;
        _document.Text.Changed += (_, _) =>
        {
            _layoutDirty = true;
            if (!_writingText)
                NotifyBoundValueChanged(TextProperty, _document.Content);
            TextChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        };
        _document.Text.CaretMoved += (_, _) =>
        {
            EnsureCaretVisible();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        };
        _document.Changed += (_, _) =>
        {
            _layoutDirty = true;
            InvalidateVisual();
        };
    }

    private bool _writingText;

    public RichTextDocument Document => _document;

    public DocumentPageSettings PageSettings { get; set; } = new();

    public IReadOnlyList<DocumentPage> Pages
    {
        get
        {
            EnsureLayout();
            return _pages;
        }
    }

    [Styled(Category = "Text", AffectsLayout = true, External = true)]
    public string? Text
    {
        get => _document.Content;
        set => SetValue(TextProperty, value ?? string.Empty);
    }
    private static string? TextDefault => string.Empty;

    private void WriteText(string? value)
    {
        _writingText = true;
        try { _document.Content = value ?? string.Empty; }
        finally { _writingText = false; }
        _layoutDirty = true;
    }

    [Styled(Category = "Text")]
    public partial bool IsReadOnly { get; set; }

    [Styled(Category = "Appearance")]
    public partial Color PageBackground { get; set; }
    private static Color PageBackgroundDefault => Colors.White;

    [Styled(Category = "Appearance")]
    public partial float PageSpacing { get; set; }
    private static float PageSpacingDefault => 24f;

    [Styled(Category = "Appearance")]
    public partial Color SelectionBrush { get; set; }
    private static Color SelectionBrushDefault => new(255, 0, 120, 215);

    public event EventHandler? TextChanged;
    public event EventHandler? SelectionChanged;

    /// <summary>Raised by <see cref="Print"/> with the pages to send to a printer.</summary>
    public event EventHandler<DocumentPrintRequest>? PrintRequested;

    public override bool AcceptsTextInput => IsEnabled && !IsReadOnly;

    public int CaretIndex => _document.Text.CaretIndex;
    public int SelectionStart => _document.Text.SelectionStart;
    public int SelectionLength => _document.Text.SelectionLength;

    public bool AcceptsComposition => throw new NotImplementedException();

    // ===== page settings helpers =====

    public void SetOrientation(PageOrientation orientation)
    {
        PageSettings.Orientation = orientation;
        _layoutDirty = true;
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void SetMargins(Thickness margins)
    {
        PageSettings.Margins = margins;
        _layoutDirty = true;
        InvalidateVisual();
    }

    public void SetPaper(PageSize paper)
    {
        PageSettings.Paper = paper;
        _layoutDirty = true;
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void SetHeader(string? text)
    {
        PageSettings.HeaderText = text;
        InvalidateVisual();
    }

    public void SetFooter(string? text)
    {
        PageSettings.FooterText = text;
        InvalidateVisual();
    }

    /// <summary>Ask the host to print. ZF does not open a system dialog itself.</summary>
    public void Print()
    {
        EnsureLayout();
        PrintRequested?.Invoke(this, new DocumentPrintRequest
        {
            Settings = PageSettings,
            Pages = _pages.ToList(),
            PlainText = _document.Content,
            Document = _document,
        });
    }

    public void ApplyStyle(RichTextStyle style) => _document.ApplyStyleToSelection(style);

    // ===== layout =====

    private void EnsureLayout()
    {
        if (!_layoutDirty) return;
        _layoutDirty = false;
        _pages.Clear();

        Font font = EffectiveFont;
        float lineHeight = TextMeasurer.LineHeight(font);
        float contentHeight = PageSettings.ContentHeight;
        // reserve header/footer bands
        float headerBand = string.IsNullOrEmpty(PageSettings.HeaderText) ? 0 : lineHeight * 1.5f;
        float footerBand = PageSettings.ShowPageNumbers || !string.IsNullOrEmpty(PageSettings.FooterText)
            ? lineHeight * 1.5f : 0;
        float usable = Math.Max(lineHeight, contentHeight - headerBand - footerBand);

        string text = _document.Content;
        string[] lines = text.Length == 0 ? [""] : text.Split('\n');
        int index = 0;
        int pageIndex = 0;
        int lineCursor = 0;

        while (lineCursor < lines.Length)
        {
            int pageStart = index;
            float used = 0;
            int linesOnPage = 0;

            while (lineCursor < lines.Length && used + lineHeight <= usable)
            {
                used += lineHeight;
                // +1 for the '\n' except after the last line of the whole text
                int lineLen = lines[lineCursor].Length;
                bool hasBreak = lineCursor < lines.Length - 1 || text.EndsWith('\n');
                index += lineLen + (lineCursor < lines.Length - 1 ? 1 : 0);
                lineCursor++;
                linesOnPage++;
                _ = hasBreak;
            }

            if (linesOnPage == 0)
            {
                // single line taller than the page — still take one line
                int lineLen = lines[lineCursor].Length;
                index += lineLen + (lineCursor < lines.Length - 1 ? 1 : 0);
                lineCursor++;
            }

            int length = Math.Max(0, index - pageStart);
            _pages.Add(new DocumentPage(pageIndex++, pageStart, length));
        }

        if (_pages.Count == 0)
            _pages.Add(new DocumentPage(0, 0, 0));
    }

    // ===== input =====

    protected override void OnTextInput(char c)
    {
        if (IsReadOnly || (char.IsControl(c) && c is not '\n')) return;
        if (c == '\r') c = '\n';
        int pos = _document.Text.SelectionStart;
        int removed = _document.Text.SelectionLength;
        _document.Text.Insert(c.ToString());
        _document.NotifyEdit(pos, removed, 1);
        _layoutDirty = true;
        ResetCaretBlink();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool ctrl = e.Modifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.Modifiers.HasFlag(KeyModifiers.Shift);

        switch (e.Key)
        {
            case Key.P when ctrl:
                Print();
                e.Handled = true;
                break;
            case Key.Backspace when !IsReadOnly:
                EditDelete(() => _document.Text.Backspace());
                e.Handled = true;
                break;
            case Key.Delete when !IsReadOnly:
                EditDelete(() => _document.Text.Delete());
                e.Handled = true;
                break;
            case Key.Left:
                _document.Text.MoveLeft(shift); e.Handled = true; break;
            case Key.Right:
                _document.Text.MoveRight(shift); e.Handled = true; break;
            case Key.Up:
                _document.Text.MoveVertical(-1, shift); e.Handled = true; break;
            case Key.Down:
                _document.Text.MoveVertical(1, shift); e.Handled = true; break;
            case Key.Home when ctrl:
                _document.Text.SetCaret(0, shift); e.Handled = true; break;
            case Key.End when ctrl:
                _document.Text.SetCaret(_document.Text.Text.Length, shift); e.Handled = true; break;
            case Key.Home:
                _document.Text.MoveToLineStart(shift); e.Handled = true; break;
            case Key.End:
                _document.Text.MoveToLineEnd(shift); e.Handled = true; break;
            case Key.A when ctrl:
                _document.Text.SelectAll(); e.Handled = true; break;
            case Key.C when ctrl:
                if (SelectionLength > 0) Clipboard.Current.SetText(_document.Text.SelectedText);
                e.Handled = true; break;
            case Key.V when ctrl && !IsReadOnly:
                if (Clipboard.Current.GetText() is { Length: > 0 } paste)
                {
                    int pos = SelectionStart;
                    int removed = SelectionLength;
                    _document.Text.Insert(paste);
                    _document.NotifyEdit(pos, removed, paste.Length);
                    _layoutDirty = true;
                }
                e.Handled = true; break;
            case Key.B when ctrl && !IsReadOnly:
                ApplyStyle(_document.GetStyleAt(SelectionStart).WithBold());
                e.Handled = true; break;
            case Key.I when ctrl && !IsReadOnly:
                ApplyStyle(_document.GetStyleAt(SelectionStart).WithItalic());
                e.Handled = true; break;
            case Key.Enter when !IsReadOnly:
                OnTextInput('\n'); e.Handled = true; break;
            case Key.Z when ctrl && !IsReadOnly:
                _document.Text.Undo(); _layoutDirty = true; e.Handled = true; break;
            case Key.Y when ctrl && !IsReadOnly:
                _document.Text.Redo(); _layoutDirty = true; e.Handled = true; break;
        }
    }

    private void EditDelete(Action action)
    {
        int before = _document.Text.Text.Length;
        int caret = _document.Text.CaretIndex;
        action();
        int after = _document.Text.Text.Length;
        if (before > after)
            _document.NotifyEdit(Math.Min(caret, _document.Text.CaretIndex), before - after, 0);
        _layoutDirty = true;
        ResetCaretBlink();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (e.Button != MouseButton.Left) return;
        Focus();
        int index = HitTest(e.Location);
        _document.Text.SetCaret(index, extendSelection: e.Modifiers.HasFlag(KeyModifiers.Shift));
        _dragging = true;
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        if (!_dragging) return;
        _document.Text.SetCaret(HitTest(e.Location), extendSelection: true);
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (e.Button == MouseButton.Left) _dragging = false;
    }

    private int HitTest(Point location)
    {
        EnsureLayout();
        Font font = EffectiveFont;
        float lineHeight = TextMeasurer.LineHeight(font);
        PageSize page = PageSettings.EffectiveSize;
        float totalPage = page.Height + PageSpacing;

        float y = location.Y + _scrollY - Padding.Top;
        int pageIndex = Math.Max(0, (int)(y / totalPage));
        pageIndex = Math.Min(pageIndex, Math.Max(0, _pages.Count - 1));

        DocumentPage docPage = _pages[pageIndex];
        float localY = y - pageIndex * totalPage - PageSettings.Margins.Top;
        int lineOnPage = Math.Max(0, (int)(localY / lineHeight));

        // map to document line among this page's lines
        string text = _document.Content;
        int pos = docPage.TextStart;
        int line = 0;
        while (pos < docPage.TextEnd && line < lineOnPage)
        {
            int nl = text.IndexOf('\n', pos, docPage.TextEnd - pos);
            if (nl < 0) { pos = docPage.TextEnd; break; }
            pos = nl + 1;
            line++;
        }

        float x = location.X - Padding.Left - PageSettings.Margins.Left;
        // approximate column by measuring
        int lineEnd = text.IndexOf('\n', pos);
        if (lineEnd < 0 || lineEnd > docPage.TextEnd) lineEnd = docPage.TextEnd;
        string lineText = text[pos..lineEnd];
        float width = 0;
        int col = 0;
        for (; col < lineText.Length; col++)
        {
            float w = TextMeasurer.Current.MeasureText(lineText[col].ToString(), font).Width;
            if (width + w / 2f >= x) break;
            width += w;
        }
        return Math.Min(pos + col, text.Length);
    }

    // ===== drawing =====

    protected override void DrawContent(Graphics g)
    {
        EnsureLayout();
        Font font = EffectiveFont;
        float lineHeight = TextMeasurer.LineHeight(font);
        PageSize pageSize = PageSettings.EffectiveSize;
        float totalPage = pageSize.Height + PageSpacing;
        Rectangle view = new(0, 0, LocalBounds.Width, LocalBounds.Height);

        g.Save();
        g.ClipRect(view);

        for (int p = 0; p < _pages.Count; p++)
        {
            float pageTop = Padding.Top - _scrollY + p * totalPage;
            if (pageTop + pageSize.Height < 0 || pageTop > view.Height) continue;

            float pageLeft = Padding.Left + Math.Max(0, (view.Width - Padding.Horizontal - pageSize.Width) / 2f);
            var pageRect = new Rectangle(pageLeft, pageTop, pageSize.Width, pageSize.Height);

            // page shadow
            g.FillRectangle(new Rectangle(pageRect.X + 3, pageRect.Y + 3, pageRect.Width, pageRect.Height),
                new Color(60, 0, 0, 0));
            g.FillRectangle(pageRect, PageBackground);

            DocumentPage page = _pages[p];
            DrawHeaderFooter(g, pageRect, p, font);

            float contentX = pageRect.X + PageSettings.Margins.Left;
            float contentY = pageRect.Y + PageSettings.Margins.Top;
            if (!string.IsNullOrEmpty(PageSettings.HeaderText))
                contentY += lineHeight * 1.5f;

            string text = _document.Content;
            int pos = page.TextStart;
            float y = contentY;

            while (pos < page.TextEnd && y < pageRect.Bottom - PageSettings.Margins.Bottom)
            {
                int nl = text.IndexOf('\n', pos, page.TextEnd - pos);
                int lineEnd = nl < 0 ? page.TextEnd : nl;
                int lineLen = lineEnd - pos;

                DrawSelectionOnLine(g, pos, lineLen, contentX, y, lineHeight, font);
                List<TextRun> runs = _document.BuildRuns(pos, lineLen, font, TextColor);
                if (runs.Count > 0)
                    g.DrawRuns(runs, new Rectangle(contentX, y, PageSettings.ContentWidth, lineHeight),
                        font, TextColor, HorizontalContentAlignment.Left, VerticalContentAlignment.Top);
                else if (lineLen > 0)
                    g.DrawText(text.Substring(pos, lineLen), new Point(contentX, y), TextColor, font);

                pos = lineEnd < page.TextEnd ? lineEnd + 1 : page.TextEnd;
                y += lineHeight;
            }

            if (IsFocused && CaretVisible && SelectionLength == 0)
                DrawCaretOnPage(g, page, pageRect, contentX, contentY, font, lineHeight);
        }

        g.Restore();
    }

    private void DrawHeaderFooter(Graphics g, Rectangle pageRect, int pageIndex, Font font)
    {
        float lineHeight = TextMeasurer.LineHeight(font);
        Color muted = new(255, 120, 120, 120);

        if (!string.IsNullOrEmpty(PageSettings.HeaderText))
        {
            g.DrawText(PageSettings.HeaderText, new Point(
                pageRect.X + PageSettings.Margins.Left,
                pageRect.Y + PageSettings.Margins.Top * 0.3f), muted, font);
        }

        string footer = PageSettings.FooterText ?? string.Empty;
        if (PageSettings.ShowPageNumbers)
            footer = string.IsNullOrEmpty(footer)
                ? $"{pageIndex + 1}"
                : $"{footer}  ·  {pageIndex + 1}";

        if (!string.IsNullOrEmpty(footer))
        {
            float w = TextMeasurer.Current.MeasureText(footer, font).Width;
            g.DrawText(footer, new Point(
                pageRect.X + (pageRect.Width - w) / 2f,
                pageRect.Bottom - PageSettings.Margins.Bottom * 0.7f - lineHeight), muted, font);
        }
    }

    private void DrawSelectionOnLine(Graphics g, int lineStart, int lineLen, float x, float y, float lineHeight, Font font)
    {
        if (SelectionLength == 0) return;
        int a = SelectionStart;
        int b = a + SelectionLength;
        int from = Math.Max(a, lineStart);
        int to = Math.Min(b, lineStart + lineLen);
        if (from >= to) return;

        float x0 = x + Measure(_document.Content, lineStart, from - lineStart, font);
        float x1 = x + Measure(_document.Content, lineStart, to - lineStart, font);
        g.FillRectangle(new Rectangle(x0, y, Math.Max(2, x1 - x0), lineHeight), SelectionBrush);
    }

    private void DrawCaretOnPage(
        Graphics g, DocumentPage page, Rectangle pageRect,
        float contentX, float contentY, Font font, float lineHeight)
    {
        int caret = CaretIndex;
        if (caret < page.TextStart || caret > page.TextEnd) return;

        string text = _document.Content;
        int pos = page.TextStart;
        float y = contentY;
        while (pos < caret && pos < page.TextEnd)
        {
            int nl = text.IndexOf('\n', pos, page.TextEnd - pos);
            if (nl < 0 || nl >= caret)
            {
                float x = contentX + Measure(text, pos, caret - pos, font);
                g.FillRectangle(new Rectangle(x, y, 1, lineHeight), TextColor);
                return;
            }
            pos = nl + 1;
            y += lineHeight;
        }
        g.FillRectangle(new Rectangle(contentX, y, 1, lineHeight), TextColor);
    }

    private static float Measure(string text, int start, int length, Font font)
    {
        if (length <= 0 || start >= text.Length) return 0;
        length = Math.Min(length, text.Length - start);
        return TextMeasurer.Current.MeasureText(text.Substring(start, length), font).Width;
    }

    private void EnsureCaretVisible()
    {
        EnsureLayout();
        float totalPage = PageSettings.EffectiveSize.Height + PageSpacing;
        int pageIndex = 0;
        for (int i = 0; i < _pages.Count; i++)
        {
            if (CaretIndex >= _pages[i].TextStart && CaretIndex <= _pages[i].TextEnd)
            {
                pageIndex = i;
                break;
            }
        }
        float pageTop = pageIndex * totalPage;
        float view = Math.Max(0, LocalBounds.Height - Padding.Vertical);
        if (pageTop < _scrollY) _scrollY = pageTop;
        else if (pageTop + totalPage > _scrollY + view)
            _scrollY = pageTop + totalPage - view;
        _scrollY = Math.Max(0, _scrollY);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureLayout();
        PageSize page = PageSettings.EffectiveSize;
        float height = _pages.Count * (page.Height + PageSpacing) + Padding.Vertical;
        return new Size(
            Math.Min(availableSize.Width, page.Width + Padding.Horizontal + 32),
            Math.Min(availableSize.Height, height));
    }

    public void SetComposition(string text, int cursor) => throw new NotImplementedException();
}
