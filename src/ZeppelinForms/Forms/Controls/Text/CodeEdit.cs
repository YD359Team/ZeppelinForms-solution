using System.Globalization;
using ZeppelinForms.Core.Text;
using ZeppelinForms.Core.Text.Syntax;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

using ZeppelinForms.Core.Globalization;

using ZeppelinForms.Forms.Controls;

namespace ZeppelinForms.Forms.Controls.Text;

/// <summary>A code editor: monospace text, line numbers, folding, search/selection
/// highlights, keyword rules and a lightweight completion list.</summary>
/// <remarks>
/// Highlighting uses <see cref="SyntaxTokenizer"/> (trie + literal blocks), not
/// a cascade of regular expressions. Completion suggests keyword lexemes that
/// share the prefix under the caret; a host can replace <see cref="CompletionProvider"/>.
/// </remarks>
public partial class CodeEdit : TextInputControl, ITextCompositionTarget
{
    private readonly TextDocument _document = new() { IsMultiline = true };
    private SyntaxTokenizer? _tokenizer;
    private float _scrollY;
    private bool _dragging;
    private readonly HashSet<int> _foldedLines = []; // start line of a fold
    private readonly List<FoldRegion> _folds = [];
    private readonly List<string> _completionItems = [];
    private int _completionIndex = -1;
    private bool _completionOpen;
    private string _completionPrefix = string.Empty;
#pragma warning disable CS0414
    private int _occurrenceStart = -1;
    private int _occurrenceLength;
#pragma warning restore CS0414

    public CodeEdit()
    {
        TabStop = true;
        SetControlDefault(FontProperty, Font.Monospace);
        SetControlDefault(PaddingProperty, new Thickness(0));
        SetControlDefault(BackgroundProperty, new Color(255, 30, 30, 30));
        SetControlDefault(TextColorProperty, new Color(255, 212, 212, 212));

        _document.Changed += (_, _) =>
        {
            Retokenize();
            RebuildFolds();
            if (!_writingText)
                NotifyBoundValueChanged(TextProperty, _document.Text);
            TextChanged?.Invoke(this, EventArgs.Empty);
            UpdateOccurrencesFromSelection();
            InvalidateVisual();
        };
        _document.CaretMoved += (_, _) =>
        {
            EnsureCaretVisible();
            UpdateOccurrencesFromSelection();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        };

        SyntaxRules = SyntaxRuleSet.CSharp();
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
        if (SelectionLength <= 0) return;
        _document.DeleteSelection();
    }

    protected override void OnLostFocus()
    {
        base.OnLostFocus();
        ClearComposition();
    }

    // ===== document / text =====

    [Styled(Category = "Text", AffectsLayout = true, External = true)]
    public string? Text
    {
        get => _document.Text;
        set => SetValue(TextProperty, value ?? string.Empty);
    }
    private static string? TextDefault => string.Empty;

    private void WriteText(string? value)
    {
        ClearComposition();
        _writingText = true;
        try { _document.Text = value ?? string.Empty; }
        finally { _writingText = false; }
    }

    public TextDocument Document => _document;

    public int CaretIndex => _document.CaretIndex;
    public int SelectionStart => _document.SelectionStart;
    public int SelectionLength => _document.SelectionLength;
    public string SelectedText => _document.SelectedText;

    [Styled(Category = "Text")]
    public partial bool IsReadOnly { get; set; }

    public event EventHandler? TextChanged;
    public event EventHandler? SelectionChanged;

    public override bool AcceptsTextInput => IsEnabled && !IsReadOnly;

    // ===== syntax =====

    private SyntaxRuleSet? _syntaxRules;

    /// <summary>Keyword and block rules. Reassigning rebuilds the tokenizer.</summary>
    public SyntaxRuleSet? SyntaxRules
    {
        get => _syntaxRules;
        set
        {
            _syntaxRules = value;
            _tokenizer = value is null ? null : new SyntaxTokenizer(value);
            Retokenize();
            RebuildFolds();
            InvalidateVisual();
        }
    }

    private void Retokenize() => _tokenizer?.Retokenize(_document.Text);

    // ===== line numbers / gutter =====

    [Styled(Category = "Gutter", AffectsLayout = true)]
    public partial bool ShowLineNumbers { get; set; }
    private static bool ShowLineNumbersDefault => true;

    [Styled(Category = "Gutter")]
    public partial Color LineNumberColor { get; set; }
    private static Color LineNumberColorDefault => new(255, 120, 120, 120);

    [Styled(Category = "Gutter")]
    public partial Color GutterBackground { get; set; }
    private static Color GutterBackgroundDefault => new(255, 37, 37, 38);

    [Styled(Category = "Gutter", AffectsLayout = true)]
    public partial float GutterWidth { get; set; }
    private static float GutterWidthDefault => 48f;

    // ===== folding =====

    [Styled(Category = "Folding")]
    public partial bool IsFoldingEnabled { get; set; }
    private static bool IsFoldingEnabledDefault => true;

    public IReadOnlyCollection<int> FoldedLineStarts => _foldedLines;

    public void ToggleFold(int lineStart)
    {
        if (!_foldedLines.Remove(lineStart))
            _foldedLines.Add(lineStart);
        InvalidateVisual();
    }

    public void ExpandAllFolds()
    {
        _foldedLines.Clear();
        InvalidateVisual();
    }

    private void RebuildFolds()
    {
        _folds.Clear();

        // Structural folds come from braces in the text, not from highlight BlockRules
        // (strings/comments also contain braces that must be ignored).
        string text = _document.Text;
        var stack = new Stack<int>();
        bool inLineComment = false;
        bool inBlockComment = false;
        bool inString = false;
        bool inChar = false;
        bool verbatim = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            char next = i + 1 < text.Length ? text[i + 1] : '\0';

            if (inLineComment)
            {
                if (c == '\n') inLineComment = false;
                continue;
            }
            if (inBlockComment)
            {
                if (c == '*' && next == '/') { inBlockComment = false; i++; }
                continue;
            }
            if (inString)
            {
                if (verbatim)
                {
                    if (c == '"' && next == '"') { i++; continue; }
                    if (c == '"') inString = false;
                }
                else
                {
                    if (c == '\\') { i++; continue; }
                    if (c == '"') inString = false;
                }
                continue;
            }
            if (inChar)
            {
                if (c == '\\') { i++; continue; }
                if (c == '\'') inChar = false;
                continue;
            }

            if (c == '/' && next == '/') { inLineComment = true; i++; continue; }
            if (c == '/' && next == '*') { inBlockComment = true; i++; continue; }
            if (c == '@' && next == '"') { inString = true; verbatim = true; i++; continue; }
            if (c == '$' && next == '"') { inString = true; verbatim = false; i++; continue; }
            if (c == '"') { inString = true; verbatim = false; continue; }
            if (c == '\'') { inChar = true; continue; }

            if (c == '{')
                stack.Push(_document.ToPosition(i).Line);
            else if (c == '}' && stack.Count > 0)
            {
                int openLine = stack.Pop();
                int closeLine = _document.ToPosition(i).Line;
                if (closeLine > openLine)
                    _folds.Add(new FoldRegion(openLine, closeLine));
            }
        }
    }

    private bool IsLineHidden(int line)
    {
        if (!IsFoldingEnabled || _foldedLines.Count == 0) return false;

        foreach (FoldRegion fold in _folds)
        {
            if (_foldedLines.Contains(fold.StartLine) &&
                line > fold.StartLine && line <= fold.EndLine)
                return true;
        }
        return false;
    }

    // ===== duplicate line / selection =====

    /// <summary>When true, Ctrl+D duplicates the current line or selection.</summary>
    [Styled(Category = "Editing")]
    public partial bool DuplicateLineEnabled { get; set; }
    private static bool DuplicateLineEnabledDefault => true;

    public void DuplicateLineOrSelection()
    {
        if (IsReadOnly) return;

        if (SelectionLength > 0)
        {
            string selected = SelectedText;
            int insertAt = SelectionStart + SelectionLength;
            _document.SetCaret(insertAt);
            _document.Insert(selected);
            return;
        }

        var (line, _) = _document.ToPosition(CaretIndex);
        int start = _document.FromPosition(line, 0);
        int end = line + 1 < _document.Lines.Length
            ? _document.FromPosition(line + 1, 0)
            : _document.Text.Length;
        string slice = _document.Text[start..end];
        if (!slice.EndsWith('\n'))
            slice += "\n";
        _document.SetCaret(end);
        _document.Insert(slice);
    }

    // ===== occurrences =====

    [Styled(Category = "Search")]
    public partial Color OccurrenceBrush { get; set; }
    private static Color OccurrenceBrushDefault => new(120, 255, 213, 79);

    [Styled(Category = "Selection")]
    public partial Color SelectionBrush { get; set; }
    private static Color SelectionBrushDefault => new(100, 0, 120, 215);

    /// <summary>Highlight every occurrence of <paramref name="term"/> (case-sensitive).</summary>
    public void HighlightOccurrences(string? term)
    {
        if (string.IsNullOrEmpty(term))
        {
            _occurrenceTerm = null;
            _occurrenceStart = -1;
            _occurrenceLength = 0;
        }
        else
        {
            _occurrenceStart = 0; // flag: search whole text
            _occurrenceLength = term.Length;
            _occurrenceTerm = term;
        }
        InvalidateVisual();
    }

    private string? _occurrenceTerm;

    private void UpdateOccurrencesFromSelection()
    {
        if (SelectionLength is > 0 and < 64)
        {
            string term = SelectedText;
            if (!term.Contains('\n') && !string.IsNullOrWhiteSpace(term))
            {
                _occurrenceTerm = term;
                _occurrenceLength = term.Length;
                _occurrenceStart = 0;
                return;
            }
        }
        // keep explicit HighlightOccurrences term if selection is empty
        if (SelectionLength == 0 && _occurrenceTerm is not null)
            return;
        if (SelectionLength == 0)
        {
            _occurrenceTerm = null;
            _occurrenceLength = 0;
        }
    }

    // ===== completion =====

    /// <summary>Produces completion items for the prefix under the caret.
    /// Default: keyword lexemes from <see cref="SyntaxRules"/> that start with the prefix.</summary>
    public Func<string, IEnumerable<string>>? CompletionProvider { get; set; }

    [Styled(Category = "Completion")]
    public partial bool IsCompletionEnabled { get; set; }
    private static bool IsCompletionEnabledDefault => true;

    public void CloseCompletion()
    {
        _completionOpen = false;
        _completionIndex = -1;
        _completionItems.Clear();
        InvalidateVisual();
    }

    private void TryOpenCompletion()
    {
        if (!IsCompletionEnabled || IsReadOnly) return;

        string prefix = PrefixAtCaret();
        if (prefix.Length == 0)
        {
            CloseCompletion();
            return;
        }

        IEnumerable<string> source = CompletionProvider?.Invoke(prefix)
            ?? DefaultKeywordCompletions(prefix);

        _completionItems.Clear();
        _completionItems.AddRange(source.Distinct().OrderBy(s => s).Take(30));
        _completionPrefix = prefix;
        _completionOpen = _completionItems.Count > 0;
        _completionIndex = _completionOpen ? 0 : -1;
        InvalidateVisual();
    }

    private IEnumerable<string> DefaultKeywordCompletions(string prefix) =>
        _syntaxRules?.KeywordLexemes
            .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        ?? [];

    private string PrefixAtCaret()
    {
        string text = _document.Text;
        int i = CaretIndex;
        while (i > 0 && (char.IsLetterOrDigit(text[i - 1]) || text[i - 1] == '_'))
            i--;
        return text[i..CaretIndex];
    }

    private void AcceptCompletion()
    {
        if (!_completionOpen || _completionIndex < 0 || _completionIndex >= _completionItems.Count)
            return;

        string item = _completionItems[_completionIndex];
        int start = CaretIndex - _completionPrefix.Length;
        _document.SetCaret(start);
        _document.SetCaret(CaretIndex + _completionPrefix.Length, extendSelection: true);
        _document.Insert(item);
        CloseCompletion();
    }

    // ===== editing =====

    protected override void OnTextInput(char c)
    {
        if (IsReadOnly || (char.IsControl(c) && c is not '\n' and not '\t')) return;
        if (c == '\r') c = '\n';
        _document.Insert(c.ToString());
        ResetCaretBlink();
        if (char.IsLetterOrDigit(c) || c == '_')
            TryOpenCompletion();
        else
            CloseCompletion();
    }


    /// <summary>Whether <see cref="Undo"/> has an edit to take back.</summary>
    public bool CanUndo => _document.CanUndo && !IsReadOnly;

    /// <summary>Whether <see cref="Redo"/> has an edit to bring back.</summary>
    public bool CanRedo => _document.CanRedo && !IsReadOnly;

    /// <summary>Take back the last edit. Ctrl+Z.</summary>
    public void Undo()
    {
        if (!IsReadOnly) _document.Undo();
    }

    /// <summary>Bring back the last undone edit. Ctrl+Y or Ctrl+Shift+Z.</summary>
    public void Redo()
    {
        if (!IsReadOnly) _document.Redo();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool ctrl = e.Modifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.Modifiers.HasFlag(KeyModifiers.Shift);

        if (_completionOpen)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    CloseCompletion();
                    e.Handled = true;
                    return;
                case Key.Up:
                    _completionIndex = Math.Max(0, _completionIndex - 1);
                    InvalidateVisual();
                    e.Handled = true;
                    return;
                case Key.Down:
                    _completionIndex = Math.Min(_completionItems.Count - 1, _completionIndex + 1);
                    InvalidateVisual();
                    e.Handled = true;
                    return;
                case Key.Tab:
                case Key.Enter:
                    AcceptCompletion();
                    e.Handled = true;
                    return;
            }
        }

        switch (e.Key)
        {
            case Key.D when ctrl && DuplicateLineEnabled && !IsReadOnly:
                DuplicateLineOrSelection();
                e.Handled = true;
                break;
            case Key.Space when ctrl && IsCompletionEnabled:
                TryOpenCompletion();
                e.Handled = true;
                break;
            case Key.Backspace when !IsReadOnly:
                _document.Backspace();
                TryOpenCompletion();
                e.Handled = true;
                break;
            case Key.Delete when !IsReadOnly:
                _document.Delete();
                e.Handled = true;
                break;
            case Key.Left when ctrl:
                _document.MoveWordLeft(shift); e.Handled = true; break;
            case Key.Right when ctrl:
                _document.MoveWordRight(shift); e.Handled = true; break;
            case Key.Left:
                _document.MoveLeft(shift); CloseCompletion(); e.Handled = true; break;
            case Key.Right:
                _document.MoveRight(shift); CloseCompletion(); e.Handled = true; break;
            case Key.Up:
                _document.MoveVertical(-1, shift); CloseCompletion(); e.Handled = true; break;
            case Key.Down:
                _document.MoveVertical(1, shift); CloseCompletion(); e.Handled = true; break;
            case Key.Home when ctrl:
                _document.SetCaret(0, shift); e.Handled = true; break;
            case Key.End when ctrl:
                _document.SetCaret(_document.Text.Length, shift); e.Handled = true; break;
            case Key.Home:
                _document.MoveToLineStart(shift); e.Handled = true; break;
            case Key.End:
                _document.MoveToLineEnd(shift); e.Handled = true; break;
            case Key.A when ctrl:
                _document.SelectAll(); e.Handled = true; break;
            case Key.C when ctrl:
                if (SelectionLength > 0) Clipboard.Current.SetText(SelectedText);
                e.Handled = true; break;
            case Key.X when ctrl && !IsReadOnly:
                if (SelectionLength > 0)
                {
                    Clipboard.Current.SetText(SelectedText);
                    _document.DeleteSelection();
                }
                e.Handled = true; break;
            case Key.V when ctrl && !IsReadOnly:
                if (Clipboard.Current.GetText() is { Length: > 0 } paste)
                    _document.Insert(paste);
                e.Handled = true; break;
            case Key.Z when ctrl && shift && !IsReadOnly:
                Redo(); e.Handled = true; break;
            case Key.Z when ctrl && !IsReadOnly:
                Undo(); e.Handled = true; break;
            case Key.Y when ctrl && !IsReadOnly:
                Redo(); e.Handled = true; break;
            case Key.Enter when !IsReadOnly:
                OnTextInput('\n'); e.Handled = true; break;
            case Key.Tab when !IsReadOnly && !_completionOpen:
                _document.Insert("    "); e.Handled = true; break;
        }
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

        Point local = ToLocal(e.Location);
        float gutter = ShowLineNumbers ? GutterWidth : 0;

        if (ShowLineNumbers && local.X < gutter)
        {
            int line = LineAtLocalY(local.Y);
            int start = _document.FromPosition(line, 0);
            int end = line + 1 < _document.Lines.Length
                ? _document.FromPosition(line + 1, 0)
                : _document.Text.Length;
            _document.SetCaret(start);
            _document.SetCaret(end, extendSelection: true);

            if (IsFoldingEnabled && local.X < Math.Max(14, GutterWidth * 0.35f))
            {
                foreach (FoldRegion fold in _folds)
                {
                    if (fold.StartLine == line)
                    {
                        ToggleFold(line);
                        break;
                    }
                }
            }

            e.Handled = true;
            return;
        }

        _document.SetCaret(IndexFromPoint(e.Location), extendSelection: e.Modifiers.HasFlag(KeyModifiers.Shift));
        _dragging = true;
        CaptureMouse();
        CloseCompletion();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        if (!_dragging) return;
        _document.SetCaret(IndexFromPoint(e.Location), extendSelection: true);
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (e.Button == MouseButton.Left)
        {
            _dragging = false;
            ReleaseMouseCapture();
        }
    }

    private int LineAtLocalY(float localY)
    {
        float lineHeight = TextMeasurer.LineHeight(EffectiveFont);
        float rel = localY + _scrollY - Padding.Top;
        int visual = Math.Max(0, (int)(rel / lineHeight));
        return VisualToDocumentLine(visual);
    }

    private int VisualToDocumentLine(int visualIndex)
    {
        int visual = 0;
        for (int line = 0; line < _document.Lines.Length; line++)
        {
            if (IsLineHidden(line)) continue;
            if (visual == visualIndex) return line;
            visual++;
        }
        return Math.Max(0, _document.Lines.Length - 1);
    }

    /// <summary>Document index under a form-space point — same contract as TextBox.IndexFromPoint.</summary>
    private int IndexFromPoint(Point absolute)
    {
        Point local = ToLocal(absolute);
        Font font = EffectiveFont;
        float gutter = ShowLineNumbers ? GutterWidth : 0;

        int line = LineAtLocalY(local.Y);
        int lineStart = _document.FromPosition(line, 0);
        string lineText = line < _document.Lines.Length ? _document.Lines[line] : string.Empty;

        float x = local.X - gutter - Padding.Left;
        if (x <= 0 || lineText.Length == 0)
            return lineStart;

        TextLineLayout layout = TextMeasurer.LayOutLine(lineText, font, Core.Text.FlowDirection.LeftToRight);
        int column = layout.IndexAt(x);
        return Math.Min(lineStart + column, _document.Text.Length);
    }

    private float MaxScrollY()
    {
        float lineHeight = TextMeasurer.LineHeight(EffectiveFont);
        int visible = 0;
        string[] lines = _document.Lines;
        for (int i = 0; i < lines.Length; i++)
            if (!IsLineHidden(i)) visible++;
        float content = visible * lineHeight + Padding.Vertical;
        return Math.Max(0, content - LocalBounds.Height);
    }


    /// <summary>The wheel scrolls by lines. At either end it goes on to the panel
    /// around the field, as it does from a list and from <see cref="TextBox"/>.</summary>
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e.Handled) return;

        float before = _scrollY;
        float lineHeight = TextMeasurer.LineHeight(EffectiveFont);
        float step = 3 * lineHeight;
        float next = _scrollY - e.Delta / 120f * step;
        float max = MaxScrollY();
        _scrollY = Math.Clamp(next, 0, max);

        if (Math.Abs(before - _scrollY) > 0.01f)
        {
            e.Handled = true;
            InvalidateVisual();
        }
    }

    // ===== drawing =====

    /// <summary>Text colour for the dark editor surface. A theme may have forced
    /// <see cref="UIElement.TextColor"/> to a near-black <c>c.Text</c>; on this
    /// control that is unreadable, so fall back to the editor default.</summary>
    private Color ResolveEditorTextColor()
    {
        Color c = TextColor;
        int y = (c.R * 299 + c.G * 587 + c.B * 114) / 1000;
        return y < 80 ? new Color(255, 212, 212, 212) : c;
    }


    protected override void DrawContent(Graphics g)
    {
        Rectangle bounds = LocalBounds;
        float gutter = ShowLineNumbers ? GutterWidth : 0;
        Font font = EffectiveFont;
        float lineHeight = TextMeasurer.LineHeight(font);
        string[] lines = _document.Lines;
        Color textColor = ResolveEditorTextColor();

        if (ShowLineNumbers)
        {
            g.FillRectangle(new Rectangle(0, 0, gutter, bounds.Height), GutterBackground);
        }

        g.Save();
        g.ClipRect(new Rectangle(gutter, 0, bounds.Width - gutter, bounds.Height));

        float y = Padding.Top - _scrollY;
        int digits = Math.Max(2, (int)Math.Log10(Math.Max(1, lines.Length)) + 1);

        for (int line = 0; line < lines.Length; line++)
        {
            if (IsLineHidden(line)) continue;

            if (y + lineHeight >= 0 && y <= bounds.Height)
            {
                int lineStart = _document.FromPosition(line, 0);
                int lineLen = lines[line].Length;

                DrawLineHighlights(g, lineStart, lineLen, gutter + Padding.Left, y, lineHeight, font);

                // syntax-coloured runs
                DrawSyntaxLine(g, lineStart, lineLen, lines[line], gutter + Padding.Left, y, lineHeight, font, textColor);

                if (ShowLineNumbers)
                {
                    // line numbers are drawn outside the text clip — restore after loop
                }
            }

            y += lineHeight;
        }

        g.Restore();

        // line numbers + fold markers over the gutter
        if (ShowLineNumbers)
        {
            y = Padding.Top - _scrollY;
            for (int line = 0; line < lines.Length; line++)
            {
                if (IsLineHidden(line)) continue;
                if (y + lineHeight >= 0 && y <= bounds.Height)
                {
                    string num = (line + 1).ToString(CultureInfo.CurrentCulture).PadLeft(digits);
                    float numX = gutter - 8 - TextMeasurer.Current.MeasureText(num, font).Width;
                    g.DrawText(num, new Point(Math.Max(4, numX), y), LineNumberColor, font);

                    if (IsFoldingEnabled && _folds.Any(f => f.StartLine == line))
                    {
                        string mark = _foldedLines.Contains(line) ? "+" : "−";
                        g.DrawText(mark, new Point(2, y), LineNumberColor, font);
                    }
                }
                y += lineHeight;
            }
        }

        if (IsFocused && CaretVisible && SelectionLength == 0)
            DrawCaret(g, gutter, font, lineHeight);

        if (_completionOpen)
            DrawCompletion(g, gutter, font, lineHeight);
    }

    private void DrawLineHighlights(Graphics g, int lineStart, int lineLen, float x0, float y, float lineHeight, Font font)
    {
        if (lineLen < 0) return;
        int take = Math.Min(lineLen, Math.Max(0, _document.Text.Length - lineStart));
        string lineText = take == 0 ? string.Empty : _document.Text.Substring(lineStart, take);
        TextLineLayout layout = TextMeasurer.LayOutLine(lineText, font, Core.Text.FlowDirection.LeftToRight);

        int selA = SelectionStart;
        int selB = selA + SelectionLength;

        if (SelectionLength > 0)
        {
            int from = Math.Max(selA, lineStart) - lineStart;
            int to = Math.Min(selB, lineStart + lineText.Length) - lineStart;
            if (from < to)
            {
                foreach ((float left, float right) in layout.SelectionBounds(from, to))
                    g.FillRectangle(new Rectangle(x0 + left, y, Math.Max(1, right - left), lineHeight), SelectionBrush);
            }
        }

        if (_occurrenceTerm is { Length: > 0 } term)
        {
            int idx = 0;
            while ((idx = lineText.IndexOf(term, idx, StringComparison.Ordinal)) >= 0)
            {
                int abs = lineStart + idx;
                if (!(abs >= selA && abs < selB))
                {
                    foreach ((float left, float right) in layout.SelectionBounds(idx, idx + term.Length))
                        g.FillRectangle(new Rectangle(x0 + left, y, Math.Max(1, right - left), lineHeight), OccurrenceBrush);
                }
                idx += term.Length;
            }
        }
    }

    private void DrawSyntaxLine(
        Graphics g, int lineStart, int lineLen, string lineText,
        float x, float y, float lineHeight, Font font, Color defaultColor)
    {
        if (lineLen == 0) return;

        // Keep a single font for the line so measure, hit-test and draw share the same advances.
        // Syntax only changes colour (TextBox lays out one font per row for the same reason).
        if (_tokenizer is null)
        {
            g.DrawText(lineText, new Point(x, y), defaultColor, font);
            return;
        }

        var runs = new List<TextRun>();
        int cursor = 0;
        while (cursor < lineLen)
        {
            int abs = lineStart + cursor;
            SyntaxStyle? style = _tokenizer.StyleAt(abs);
            int runEnd = cursor + 1;
            while (runEnd < lineLen && SameForeground(_tokenizer.StyleAt(lineStart + runEnd), style))
                runEnd++;

            runs.Add(new TextRun(lineText[cursor..runEnd])
            {
                Font = font,
                Color = style?.Foreground ?? defaultColor,
            });
            cursor = runEnd;
        }

        if (runs.Count > 0)
            g.DrawRuns(runs, new Rectangle(x, y, Math.Max(1, LocalBounds.Width), lineHeight),
                font, defaultColor, HorizontalContentAlignment.Left, VerticalContentAlignment.Top);
    }

    private static bool SameForeground(SyntaxStyle? a, SyntaxStyle? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return a is null && b is null;
        return a.Foreground == b.Foreground;
    }

    private void DrawCaret(Graphics g, float gutter, Font font, float lineHeight)
    {
        var (line, col) = _document.ToPosition(CaretIndex);
        if (IsLineHidden(line)) return;

        int visualLine = 0;
        for (int i = 0; i < line; i++)
            if (!IsLineHidden(i)) visualLine++;

        float y = Padding.Top - _scrollY + visualLine * lineHeight;
        string lineText = line < _document.Lines.Length ? _document.Lines[line] : string.Empty;
        TextLineLayout layout = TextMeasurer.LayOutLine(lineText, font, Core.Text.FlowDirection.LeftToRight);
        float x = gutter + Padding.Left + layout.CaretX(Math.Clamp(col, 0, lineText.Length));

        if (IsComposing)
        {
            float compW = TextMeasurer.Current.MeasureTextWidth(_composition, _composition.Length, font);
            g.DrawText(_composition, new Point(x, y), TextColor, font);
            g.DrawRectangle(new Rectangle(x, y + lineHeight - 2, Math.Max(2, compW), 1), TextColor, 1);
            int cc = Math.Min(_compositionCursor, _composition.Length);
            x += TextMeasurer.Current.MeasureTextWidth(_composition, cc, font);
        }

        g.FillRectangle(new Rectangle(x, y, 1, lineHeight), TextColor);

        if (IsFocused)
            ReportTextInputArea(new Rectangle(x, y, 1, lineHeight));
    }

    private void DrawCompletion(Graphics g, float gutter, Font font, float lineHeight)
    {
        var (line, col) = _document.ToPosition(CaretIndex);
        int visualLine = 0;
        for (int i = 0; i < line; i++)
            if (!IsLineHidden(i)) visualLine++;

        float y = Padding.Top - _scrollY + (visualLine + 1) * lineHeight;
        int lineStart = _document.FromPosition(line, 0);
        float x = gutter + Padding.Left + Measure(_document.Text, lineStart, col, font);

        float width = 220;
        float height = Math.Min(8, _completionItems.Count) * lineHeight + 4;
        var box = new Rectangle(x, y, width, height);
        g.FillRectangle(box, new Color(255, 45, 45, 48));
        g.DrawRectangle(box, new Color(255, 70, 70, 70), 1);

        for (int i = 0; i < _completionItems.Count && i < 8; i++)
        {
            float iy = y + 2 + i * lineHeight;
            if (i == _completionIndex)
                g.FillRectangle(new Rectangle(x + 1, iy, width - 2, lineHeight), new Color(255, 0, 122, 204));
            g.DrawText(_completionItems[i], new Point(x + 6, iy), TextColor, font);
        }
    }

    private static float Measure(string text, int start, int length, Font font)
    {
        if (length <= 0 || start >= text.Length) return 0;
        length = Math.Min(length, text.Length - start);
        // Prefix width of the line segment — same basis TextBox uses via LayOutLine
        string piece = text.Substring(start, length);
        return TextMeasurer.Current.MeasureTextWidth(piece, piece.Length, font);
    }

    private void EnsureCaretVisible()
    {
        float lineHeight = TextMeasurer.LineHeight(EffectiveFont);
        var (line, _) = _document.ToPosition(CaretIndex);
        int visual = 0;
        for (int i = 0; i < line; i++)
            if (!IsLineHidden(i)) visual++;

        float caretY = visual * lineHeight;
        float view = Math.Max(0, LocalBounds.Height);
        if (caretY < _scrollY) _scrollY = caretY;
        else if (caretY + lineHeight > _scrollY + view) _scrollY = caretY + lineHeight - view;
        _scrollY = Math.Max(0, _scrollY);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(Math.Min(availableSize.Width, 400), Math.Min(availableSize.Height, 240));

    private readonly record struct FoldRegion(int StartLine, int EndLine);
    /// <inheritdoc />
    protected override List<MenuItem>? GetDefaultContextMenu()
    {
        if (!IsEnabled) return null;

        bool selection = SelectionLength > 0;
        bool editable = !IsReadOnly;

        return
        [
            Command(ZfText.Undo, CanUndo, Undo, Key.Z),
            Command(ZfText.Redo, CanRedo, Redo, Key.Y),
            MenuItem.Separator,
            Command(ZfText.Cut, editable && selection, CutSelection, Key.X),
            Command(ZfText.Copy, selection, CopySelection, Key.C),
            Command(ZfText.Paste, editable, Paste, Key.V),
            MenuItem.Separator,
            Command(ZfText.SelectAll, _document.Text.Length > 0, () => _document.SelectAll(), Key.A),
        ];

        static MenuItem Command(TextKey caption, bool enabled, Action run, Key key, KeyModifiers modifiers = KeyModifiers.Control)
        {
            var item = new MenuItem
            {
                Text = Localization.Get(caption),
                IsEnabled = enabled,
                InputGestureText = new KeyGesture(key, modifiers).ToString(),
            };
            item.Click += (_, _) => run();
            return item;
        }
    }

    private void CopySelection()
    {
        if (SelectionLength > 0)
            Clipboard.Current.SetText(SelectedText);
    }

    private void CutSelection()
    {
        if (IsReadOnly || SelectionLength == 0) return;
        Clipboard.Current.SetText(SelectedText);
        _document.DeleteSelection();
    }

    private void Paste()
    {
        if (IsReadOnly) return;
        if (Clipboard.Current.GetText() is { Length: > 0 } text)
            _document.Insert(text);
    }

}
