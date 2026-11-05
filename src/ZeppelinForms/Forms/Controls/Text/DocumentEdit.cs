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
    /// <summary>Wrapped lines: start index and length in the document text.</summary>
    private readonly List<(int Start, int Length)> _visualLines = [];
    private float _headerBand;
    private float _footerBand;
    /// <summary>Index into <see cref="_visualLines"/> for the first line of each page.</summary>
    private readonly List<int> _pageVisualStart = [];
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
        _layoutDirty = true;
    }

    protected override void OnLostFocus()
    {
        base.OnLostFocus();
        ClearComposition();
    }

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
        ClearComposition();
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
        _visualLines.Clear();

        Font font = EffectiveFont;
        float lineHeight = TextMeasurer.LineHeight(font);
        float contentWidth = Math.Max(1, PageSettings.ContentWidth);
        float contentHeight = PageSettings.ContentHeight;

        _headerBand = string.IsNullOrEmpty(PageSettings.HeaderText) ? 0 : lineHeight * 1.5f;
        _footerBand = PageSettings.ShowPageNumbers || !string.IsNullOrEmpty(PageSettings.FooterText)
            ? lineHeight * 1.5f : 0;
        float usable = Math.Max(lineHeight, contentHeight - _headerBand - _footerBand);
        int maxLinesPerPage = Math.Max(1, (int)(usable / lineHeight));

        string text = _document.Content;
        // Build wrapped visual lines across the whole document
        int i = 0;
        while (i <= text.Length)
        {
            if (i == text.Length)
            {
                if (text.Length == 0 || text.EndsWith('\n'))
                    _visualLines.Add((i, 0));
                break;
            }

            int hardEnd = text.IndexOf('\n', i);
            if (hardEnd < 0) hardEnd = text.Length;
            int hardLen = hardEnd - i;

            if (hardLen == 0)
            {
                _visualLines.Add((i, 0));
            }
            else
            {
                int lineStart = i;
                while (lineStart < hardEnd)
                {
                    int take = FitWidth(text, lineStart, hardEnd - lineStart, contentWidth, font);
                    if (take <= 0) take = 1; // always advance
                    _visualLines.Add((lineStart, take));
                    lineStart += take;
                }
            }

            i = hardEnd < text.Length ? hardEnd + 1 : text.Length;
            if (i >= text.Length)
            {
                if (text.EndsWith('\n'))
                    _visualLines.Add((text.Length, 0));
                break;
            }
        }

        if (_visualLines.Count == 0)
            _visualLines.Add((0, 0));

        // Pack visual lines into pages
        _pageVisualStart.Clear();
        int v = 0;
        int pageIndex = 0;
        while (v < _visualLines.Count)
        {
            int pageLineStart = v;
            int linesOnPage = Math.Min(maxLinesPerPage, _visualLines.Count - v);
            v += linesOnPage;
            _pageVisualStart.Add(pageLineStart);

            var first = _visualLines[pageLineStart];
            var last = _visualLines[v - 1];
            int textStart = first.Start;
            int textEnd = last.Start + last.Length;
            if (textEnd < text.Length && text[textEnd] == '\n')
                textEnd++;
            _pages.Add(new DocumentPage(pageIndex++, textStart, Math.Max(0, textEnd - textStart)));
        }

        if (_pages.Count == 0)
        {
            _pages.Add(new DocumentPage(0, 0, 0));
            _pageVisualStart.Add(0);
        }
    }

    /// <summary>How many characters from <paramref name="start"/> fit in <paramref name="maxWidth"/>.</summary>
    private static int FitWidth(string text, int start, int available, float maxWidth, Font font)
    {
        if (available <= 0) return 0;
        available = Math.Min(available, text.Length - start);
        float full = TextMeasurer.Current.MeasureTextWidth(text.Substring(start, available), available, font);
        if (full <= maxWidth) return available;

        // binary search the longest prefix that fits
        int lo = 1, hi = available, best = 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            float w = TextMeasurer.Current.MeasureTextWidth(text.Substring(start, mid), mid, font);
            if (w <= maxWidth) { best = mid; lo = mid + 1; }
            else hi = mid - 1;
        }

        // prefer breaking at a space when the line was wrapped
        if (best < available)
        {
            int space = text.LastIndexOf(' ', start + best - 1, best);
            if (space >= start + 1)
                best = space - start + 1; // include the space at end of this visual line
        }
        return Math.Max(1, best);
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
        if (e.Button == MouseButton.Left) _dragging = false;
    }

    /// <summary>Document index under a form-space point — same contract as TextBox.IndexFromPoint.</summary>
    private int IndexFromPoint(Point absolute)
    {
        EnsureLayout();
        Point local = ToLocal(absolute);
        Font font = EffectiveFont;
        float lineHeight = TextMeasurer.LineHeight(font);
        PageSize pageSize = PageSettings.EffectiveSize;
        float totalPage = pageSize.Height + PageSpacing;
        float viewWidth = LocalBounds.Width;

        float pageLeft = Padding.Left + Math.Max(0, (viewWidth - Padding.Horizontal - pageSize.Width) / 2f);

        // y relative to the first page's top, including scroll
        float y = local.Y + _scrollY - Padding.Top;
        int pageIndex = Math.Max(0, (int)(y / totalPage));
        pageIndex = Math.Min(pageIndex, Math.Max(0, _pages.Count - 1));

        float localY = y - pageIndex * totalPage - PageSettings.Margins.Top - _headerBand;
        int lineOnPage = Math.Max(0, (int)(localY / lineHeight));

        int pageVisStart = pageIndex < _pageVisualStart.Count ? _pageVisualStart[pageIndex] : 0;
        int pageVisEnd = pageIndex + 1 < _pageVisualStart.Count
            ? _pageVisualStart[pageIndex + 1]
            : _visualLines.Count;
        if (pageVisEnd <= pageVisStart)
            return pageIndex < _pages.Count ? _pages[pageIndex].TextStart : 0;

        int visualIndex = Math.Clamp(pageVisStart + lineOnPage, pageVisStart, pageVisEnd - 1);

        var (lineStart, lineLen) = _visualLines[visualIndex];
        string text = _document.Content;
        float x = local.X - pageLeft - PageSettings.Margins.Left;

        if (lineLen <= 0 || x <= 0)
            return lineStart;

        int take = Math.Min(lineLen, Math.Max(0, text.Length - lineStart));
        string lineText = take == 0 ? string.Empty : text.Substring(lineStart, take);
        TextLineLayout layout = TextMeasurer.LayOutLine(lineText, font, Core.Text.FlowDirection.LeftToRight);
        int column = layout.IndexAt(x);
        return Math.Min(lineStart + column, text.Length);
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
            float contentY = pageRect.Y + PageSettings.Margins.Top + _headerBand;

            string text = _document.Content;
            float y = contentY;
            float contentBottom = pageRect.Bottom - PageSettings.Margins.Bottom - _footerBand;

            int visualStart = p < _pageVisualStart.Count ? _pageVisualStart[p] : 0;
            int visualEnd = p + 1 < _pageVisualStart.Count ? _pageVisualStart[p + 1] : _visualLines.Count;

            g.Save();
            g.ClipRect(new Rectangle(contentX, contentY, PageSettings.ContentWidth, Math.Max(0, contentBottom - contentY)));

            for (int vi = visualStart; vi < visualEnd && y < contentBottom; vi++)
            {
                var (lineStart, lineLen) = _visualLines[vi];
                DrawSelectionOnLine(g, lineStart, lineLen, contentX, y, lineHeight, font);
                if (lineLen > 0)
                {
                    List<TextRun> runs = _document.BuildRuns(lineStart, lineLen, font, TextColor);
                    if (runs.Count > 0)
                        g.DrawRuns(runs, new Rectangle(contentX, y, PageSettings.ContentWidth, lineHeight),
                            font, TextColor, HorizontalContentAlignment.Left, VerticalContentAlignment.Top);
                    else
                        g.DrawText(text.Substring(lineStart, lineLen), new Point(contentX, y), TextColor, font);
                }
                y += lineHeight;
            }

            g.Restore();

            if (IsFocused && CaretVisible && SelectionLength == 0)
                DrawCaretOnPage(g, page, pageRect, contentX, contentY, font, lineHeight, visualStart, visualEnd);
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
        if (SelectionLength <= 0 || lineLen <= 0) return;

        int take = Math.Min(lineLen, Math.Max(0, _document.Content.Length - lineStart));
        string lineText = take == 0 ? string.Empty : _document.Content.Substring(lineStart, take);
        TextLineLayout layout = TextMeasurer.LayOutLine(lineText, font, Core.Text.FlowDirection.LeftToRight);

        int selA = SelectionStart;
        int selB = selA + SelectionLength;
        int from = Math.Max(selA, lineStart) - lineStart;
        int to = Math.Min(selB, lineStart + lineText.Length) - lineStart;
        if (from >= to) return;

        foreach ((float left, float right) in layout.SelectionBounds(from, to))
            g.FillRectangle(new Rectangle(x + left, y, Math.Max(1, right - left), lineHeight), SelectionBrush);
    }

    private void DrawCaretOnPage(
        Graphics g, DocumentPage page, Rectangle pageRect,
        float contentX, float contentY, Font font, float lineHeight,
        int visualStart, int visualEnd)
    {
        int caret = CaretIndex;
        if (caret < page.TextStart || caret > page.TextEnd) return;

        string text = _document.Content;
        float y = contentY;
        float x = contentX;
        bool found = false;

        for (int vi = visualStart; vi < visualEnd; vi++)
        {
            var (lineStart, lineLen) = _visualLines[vi];
            int lineEnd = lineStart + lineLen;
            // caret on this visual line (or at its end before a wrap)
            if (caret >= lineStart && caret <= lineEnd)
            {
                int col = caret - lineStart;
                x = contentX + (col <= 0 ? 0 : TextMeasurer.Current.MeasureTextWidth(text.Substring(lineStart, col), col, font));
                found = true;
                break;
            }
            y += lineHeight;
        }
        if (!found) return;

        if (IsComposing)
        {
            float compW = TextMeasurer.Current.MeasureText(_composition, font).Width;
            g.DrawText(_composition, new Point(x, y), TextColor, font);
            g.DrawRectangle(new Rectangle(x, y + lineHeight - 2, Math.Max(2, compW), 1), TextColor, 1);
            x += TextMeasurer.Current.MeasureText(
                _composition[..Math.Min(_compositionCursor, _composition.Length)], font).Width;
        }

        g.FillRectangle(new Rectangle(x, y, 1, lineHeight), TextColor);

        if (IsFocused)
            ReportTextInputArea(new Rectangle(x, y, 1, lineHeight));
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
}
