using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ZeppelinForms.Core.Globalization;
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

public partial class TextBox : TextInputControl, ITextElement, ITextCompositionTarget
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

    /// <summary>The result of the last check the field shows.</summary>
    /// <remarks>
    /// <para>
    /// A field shows a result only once there is something to judge: after the user
    /// has changed it and left it, or after <see cref="Validate"/> was called — say,
    /// by a form's submit button. Leaving a field nobody typed into shows nothing;
    /// it used to validate on any loss of focus, and an empty e-mail field, which
    /// the validator accepts, lit up green before anything was typed.
    /// </para>
    /// <para>
    /// While a result is shown the field is checked again on every change, by the
    /// rule "praise at once, blame late": an error goes away the moment the text is
    /// right, but a correct field being retyped doesn't turn red halfway through an
    /// address — it goes neutral, and an error shows when the user leaves it.
    /// </para>
    /// <para>
    /// <see cref="ValidationState.Success"/> is shown only for a field with text:
    /// an empty field the validator accepts — an optional one — has nothing to
    /// praise, and stays <see cref="ValidationState.None"/>.
    /// </para>
    /// </remarks>

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

    /// <summary>The user has changed the text since the field was created or
    /// <see cref="ResetValidation"/> was called. Leaving a field nobody typed into
    /// doesn't validate it, see <see cref="OnLostFocus"/>.</summary>
    private bool _edited;

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
            {
                _edited = true;
                OnTextEdited();
            }

            RevalidateWhileShown();

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
        // text set from code ends whatever an input method was composing over the old one
        ClearComposition();

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

    /// <summary>Where the text stands across its room. A styled property: a style
    /// sheet sets it, and a change redraws at once.</summary>
    [Styled(Category = "Text")]
    public partial HorizontalContentAlignment HorizontalContentAlign { get; set; }
    private static HorizontalContentAlignment HorizontalContentAlignDefault => HorizontalContentAlignment.Left;

    /// <summary>Where the text stands up and down in its room, when the control is
    /// taller than its text.</summary>
    [Styled(Category = "Text")]
    public partial VerticalContentAlignment VerticalContentAlign { get; set; }
    private static VerticalContentAlignment VerticalContentAlignDefault => VerticalContentAlignment.Top;

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

    // ===== clipboard and history =====
    // the keys and the context menu go through these, so both behave alike

    /// <summary>Whether <see cref="Undo"/> has an edit to take back.</summary>
    public bool CanUndo => _document.CanUndo && !IsReadOnly;

    /// <summary>Whether <see cref="Redo"/> has an edit to bring back.</summary>
    public bool CanRedo => _document.CanRedo && !IsReadOnly;

    /// <summary>Copy the selection. Nothing under a password mask: what the field hides
    /// must not leave through the clipboard.</summary>
    public void Copy()
    {
        if (_document.SelectionLength > 0 && PasswordChar is null)
            Clipboard.Current.SetText(_document.SelectedText);
    }

    /// <summary>Copy the selection and delete it.</summary>
    public void Cut()
    {
        if (IsReadOnly || _document.SelectionLength == 0 || PasswordChar is not null) return;

        Clipboard.Current.SetText(_document.SelectedText);
        _document.DeleteSelection();
    }

    public void Undo()
    {
        if (!IsReadOnly) _document.Undo();
    }

    public void Redo()
    {
        if (!IsReadOnly) _document.Redo();
    }

    /// <summary>Undo and redo, cut, copy and paste, delete and select all — as a text
    /// field has them everywhere. Each item is enabled by what it can do right now.
    /// An application's own <see cref="UIElement.ContextMenu"/> replaces it.</summary>
    protected override List<MenuItem>? GetDefaultContextMenu()
    {
        if (!IsEnabled) return null;

        bool selection = _document.SelectionLength > 0;
        bool masked = PasswordChar is not null;
        bool editable = !IsReadOnly;

        // the shortcuts are shown, not run by the menu: the field takes these keys
        // itself while it has the focus
        return
        [
            Command(ZfText.Undo, CanUndo, Undo, Key.Z),
            Command(ZfText.Redo, CanRedo, Redo, Key.Y),
            MenuItem.Separator,
            Command(ZfText.Cut, editable && selection && !masked, Cut, Key.X),
            Command(ZfText.Copy, selection && !masked, Copy, Key.C),
            Command(ZfText.Paste, editable, Paste, Key.V),
            Command(ZfText.Delete, editable && selection, () => _document.DeleteSelection(), Key.Delete, KeyModifiers.None),
            MenuItem.Separator,
            Command(ZfText.SelectAll, _document.Length > 0, SelectAll, Key.A),
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

    /// <summary>Check the content now and show the result, whether or not the user
    /// has typed anything — a form's submit button calls it on every field, and a
    /// required field left empty shows its error.</summary>
    public bool Validate()
    {
        if (Validator is null)
        {
            SetValidation(ValidationState.None, null);
            ShowErrorToolTip(null);
            return true;
        }

        string? error = Validator(_document.Text);

        // an accepted empty field is an optional one left alone: nothing to praise
        ValidationState state = error is not null ? ValidationState.Error
            : _document.Text.Length > 0 ? ValidationState.Success
            : ValidationState.None;

        SetValidation(state, error);

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

    /// <summary>Forget the check: no result is shown, and the field is validated
    /// again only after the user changes and leaves it, as a new one. For a form
    /// that is cleared to be filled anew.</summary>
    public void ResetValidation()
    {
        _edited = false;

        SetValidation(ValidationState.None, null);
        ShowErrorToolTip(null);
    }

    /// <summary>The text changed while a result is shown: "praise at once, blame
    /// late". A fixed error clears on the spot; a new error waits for the user to
    /// leave the field, the field goes neutral meanwhile. Nothing shown — nothing to
    /// update: the first result comes on leaving the field or from Validate.</summary>
    private void RevalidateWhileShown()
    {
        if (ValidationState == ValidationState.None || Validator is null) return;

        string? error = Validator(_document.Text);

        if (error is null)
        {
            Validate();
            return;
        }

        // still wrong: the message follows the text; it was right: neutral for now
        if (ValidationState == ValidationState.Error)
        {
            SetValidation(ValidationState.Error, error);
            ShowErrorToolTip(error);
        }
        else
        {
            SetValidation(ValidationState.None, null);
            ShowErrorToolTip(null);
        }
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
            // the scroll bars take their room from the text: no character under a bar
            // (see TextBox.Scrolling)
            ResolveBars();

            Rectangle column = TextColumn;

            return new Rectangle(
                column.Position,
                new Size(
                    Math.Max(0, column.Width - (_verticalBar ? ScrollBarThickness : 0)),
                    Math.Max(0, column.Height - (_horizontalBar ? ScrollBarThickness : 0))));
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

    private float LineHeight => TextMeasurer.LineHeight(EffectiveFont);

    // the mask is built by visible characters: one emoji — one dot
    private string DisplayText => PasswordChar is char pc && !IsMultiline
        ? new string(pc, _document.Length)
        : _document.Text;

    /// <summary>Index in the displayed line → index in the text: the mask has a dot per
    /// visible character, which may be several chars of the text.</summary>
    private int FromDisplayIndex(int displayIndex)
    {
        if (PasswordChar is null || IsMultiline) return displayIndex;

        string text = _document.Text;
        int index = 0;

        for (int i = 0; i < displayIndex && index < text.Length; i++)
            index = TextElements.Next(text, index);

        return index;
    }

    /// <summary>Index in the text → index in the displayed line. They differ under the password mask.</summary>
    private int ToDisplayIndex(int textIndex) =>
        PasswordChar is not null && !IsMultiline
            ? TextElements.Count(_document.Text[..Math.Min(textIndex, _document.Text.Length)])
            : textIndex;

    // ===== input method composition =====

    /// <summary>The text an input method is composing at the caret: shown underlined in
    /// the field, not in the text until it is committed.</summary>
    private string _composition = string.Empty;

    /// <summary>Where the input method's cursor stands inside the composition.</summary>
    private int _compositionCursor;

    private bool IsComposing => _composition.Length > 0;

    bool ITextCompositionTarget.AcceptsComposition => PasswordChar is null && !IsReadOnly && IsEnabled;

    void ITextCompositionTarget.SetComposition(string text, int cursor)
    {
        // the composition takes the selection's place, as typing does
        if (text.Length > 0 && !IsComposing && _document.SelectionLength > 0)
            _document.DeleteSelection();

        _composition = text;
        _compositionCursor = cursor;

        // a wrapped field may gain or lose a row
        if (IsMultiline) Invalidate();
        else InvalidateVisual();
    }

    /// <summary>Drop the composition the field shows: a click elsewhere, focus gone,
    /// text set from code. The input method finishes or cancels on its side.</summary>
    private void ClearComposition()
    {
        if (!IsComposing) return;

        _composition = string.Empty;
        _compositionCursor = 0;

        if (IsMultiline) Invalidate();
        else InvalidateVisual();
    }

    /// <summary>The text with the composition at the caret: what the rows are made of.</summary>
    private string WithComposition(string text) =>
        IsComposing && PasswordChar is null
            ? text.Insert(Math.Min(_document.CaretIndex, text.Length), _composition)
            : text;

    /// <summary>A position in the shown text — the composition in it — back in the text.</summary>
    private int WithoutComposition(int shown)
    {
        if (!IsComposing) return shown;

        int caret = _document.CaretIndex;

        if (shown <= caret) return shown;

        return Math.Max(caret, shown - _composition.Length);
    }

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
    private FlowDirection _rowsDirection;

    /// <summary>The rows laid out as they are drawn, made on first request.</summary>
    private TextLineLayout?[]? _rowLayouts;

    /// <summary>The rows at a width. Kept until the text, the width or the font changes:
    /// drawing, hit testing and the arrow keys all ask, often several times a frame.</summary>
    private IReadOnlyList<TextRow> Rows(float width)
    {
        string text = WithComposition(IsMultiline ? _document.Text : DisplayText);
        bool wrap = WordWrap && IsMultiline && width > 0 && float.IsFinite(width);
        float key = wrap ? width : float.PositiveInfinity;
        Font font = EffectiveFont;

        FlowDirection direction = EffectiveFlowDirection;

        if (_rows is not null && _rowsWidth == key && Equals(_rowsFont, font) && _rowsDirection == direction &&
            string.Equals(_rowsText, text, StringComparison.Ordinal))
            return _rows;

        var rows = new List<TextRow>();

        if (!IsMultiline)
        {
            rows.Add(new TextRow(0, text, EndsLine: true));
        }
        else
        {
            int lineStart = 0;

            // the breaks of a line that didn't change are taken from the last layout:
            // a keystroke breaks one line again, not the whole text (see TextBox.Scrolling)
            WrapCache? cache = wrap ? WrapCacheFor(width, font, direction) : null;
            var breaks = wrap ? new Dictionary<string, List<(int Start, int Length)>>(StringComparer.Ordinal) : null;

            foreach (string line in text.Split('\n'))
            {
                if (wrap && line.Length > 0)
                {
                    if (!breaks!.TryGetValue(line, out List<(int Start, int Length)>? pieces) &&
                        !cache!.Lines.TryGetValue(line, out pieces))
                        pieces = TextWrapper.Wrap(line, width, font);

                    breaks[line] = pieces;
                    AddPieces(line, lineStart, pieces, rows);
                }
                else
                {
                    rows.Add(new TextRow(lineStart, line, EndsLine: true));
                }

                lineStart += line.Length + 1;
            }

            if (cache is not null) cache.Lines = breaks!;
        }

        _rows = rows;
        _rowsText = text;
        _rowsWidth = key;
        _rowsFont = font;
        _rowsDirection = direction;
        _rowLayouts = null;

        return rows;
    }

    /// <summary>A row as it stands on the screen: the runs of its directions and the box
    /// of every character. Carets, selections, clicks and the arrow keys go by it — in a
    /// row that mixes Latin and Hebrew a column doesn't map to an x by adding widths.</summary>
    private TextLineLayout RowLayout(IReadOnlyList<TextRow> rows, int index)
    {
        if (!ReferenceEquals(rows, _rows))
            return TextMeasurer.LayOutLine(rows[index].Text, EffectiveFont, EffectiveFlowDirection);

        _rowLayouts ??= new TextLineLayout?[rows.Count];

        return _rowLayouts[index] ??= TextMeasurer.LayOutLine(rows[index].Text, EffectiveFont, EffectiveFlowDirection);
    }

    /// <summary>The alignment the rows take: <see cref="HorizontalContentAlign"/> read from
    /// the start of the text — in a right-to-left field Left is the right edge, where
    /// typing begins.</summary>
    private HorizontalContentAlignment RowAlignment => !IsRightToLeft
        ? HorizontalContentAlign
        : HorizontalContentAlign switch
        {
            HorizontalContentAlignment.Left => HorizontalContentAlignment.Right,
            HorizontalContentAlignment.Right => HorizontalContentAlignment.Left,
            var other => other,
        };

    /// <summary>Where a row's left edge stands in the text area, before scrolling. A row
    /// wider than the area starts at its left edge and scrolls.</summary>
    private float RowShift(TextLineLayout layout, float width)
    {
        float free = width - layout.Width;

        if (free <= 0) return 0;

        return RowAlignment switch
        {
            HorizontalContentAlignment.Right => free,
            HorizontalContentAlignment.Center => free / 2f,
            _ => 0,
        };
    }

    private static bool IsSpace(char c) => c is ' ' or '\t';

    /// <summary>Break one line of the text into rows no wider than <paramref name="width"/>:
    /// after spaces, which hang at the end of their row, as <see cref="TextWrapper"/> breaks
    /// the label's lines too.</summary>
    private static void Wrap(string line, int offset, float width, Font font, List<TextRow> rows) =>
        AddPieces(line, offset, TextWrapper.Wrap(line, width, font), rows);

    /// <summary>The rows of one line from its breaks.</summary>
    private static void AddPieces(string line, int offset, List<(int Start, int Length)> pieces, List<TextRow> rows)
    {
        for (int i = 0; i < pieces.Count; i++)
        {
            var (start, length) = pieces[i];
            rows.Add(new TextRow(offset + start, line.Substring(start, length), EndsLine: i == pieces.Count - 1));
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
    /// <remarks>By the row's layout: the nearer edge of the character under the x,
    /// in whichever direction it runs. It used to be the first column whose prefix
    /// reached the x, which in a right-to-left run is the wrong end.</remarks>
    private static int ColumnAt(TextRow row, TextLineLayout layout, float x)
    {
        string text = row.Text;

        // between visible characters only: never inside a surrogate pair
        int column = layout.IndexAt(x);

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

        float width = TextBounds.Width;
        TextLineLayout from = RowLayout(rows, row);
        TextLineLayout to = RowLayout(rows, target);

        _desiredX ??= RowShift(from, width) + from.CaretX(column);

        SetCaretByRows(rows[target].Start + ColumnAt(rows[target], to, _desiredX.Value - RowShift(to, width)), extend);
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

    /// <summary>Left and Right: across the screen. In a row of one direction that is along
    /// the text — forwards for Right in left-to-right text, backwards in right-to-left; in a
    /// row that mixes directions the caret goes to the next position on the screen, across
    /// the runs as they are drawn. Past the row's end it goes on along the text.</summary>
    private void MoveAcross(bool right, bool extend)
    {
        IReadOnlyList<TextRow> rows = Rows(TextBounds.Width);

        if (PasswordChar is null || IsMultiline)
        {
            var (row, column) = RowOf(rows, _document.CaretIndex);
            TextLineLayout layout = RowLayout(rows, row);

            bool mixed = false;

            foreach (TextLineRun run in layout.Runs)
                mixed |= run.IsRightToLeft != layout.IsRightToLeft;

            if (mixed)
            {
                if (layout.MoveVisually(column, right) is { } target)
                {
                    _document.SetCaret(rows[row].Start + target, extend);
                    return;
                }

                // at the row's visual end: on to the next row in the paragraph's direction,
                // or nowhere — going on along the text would turn back into the row
                bool forward = right != layout.IsRightToLeft;
                int next = row + (forward ? 1 : -1);

                if (next >= 0 && next < rows.Count)
                {
                    TextRow to = rows[next];

                    _document.SetCaret(forward
                        ? to.Start
                        : to.Start + (to.EndsLine ? to.Text.Length : TextElements.Previous(to.Text, to.Text.Length)),
                        extend);
                }

                return;
            }
        }

        // along the text, in the direction the paragraph runs
        if (right != IsRightToLeft) _document.MoveRight(extend);
        else _document.MoveLeft(extend);
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

        ClearComposition();

        // validate when leaving the field, not on every character:
        // otherwise half of a typed address would turn red. And only a field the
        // user has changed, or one already showing a result: passing through an
        // empty field with Tab is not a reason to judge it
        if (_edited || ValidationState != ValidationState.None)
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
        // while an input method composes, the keys are its: arrows move inside the
        // composition, Enter commits it, Escape cancels it — none of them edits the text
        if (IsComposing)
        {
            e.Handled = true;
            return;
        }

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
                MoveAcross(right: false, shift);
                break;

            case Key.Right:
                MoveAcross(right: true, shift);
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
                Copy();
                break;

            case Key.X when ctrl && !IsReadOnly:
                Cut();
                break;

            case Key.V when ctrl && !IsReadOnly:
                Paste();
                break;

            case Key.Z when ctrl && !shift && !IsReadOnly:
                Undo();
                break;

            case Key.Y when ctrl && !IsReadOnly:
                Redo();
                break;

            // Ctrl+Shift+Z is redo by the common convention;
            // this branch used to call Undo, the same as plain Ctrl+Z
            case Key.Z when ctrl && shift && !IsReadOnly:
                Redo();
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
    public void Paste()
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

        int index = IsMultiline ? Math.Clamp((int)(localY / LineHeight), 0, rows.Count - 1) : 0;
        TextRow row = rows[index];
        TextLineLayout layout = RowLayout(rows, index);
        int column = ColumnAt(row, layout, localX - RowShift(layout, text.Width));

        return WithoutComposition(IsMultiline ? row.Start + column : FromDisplayIndex(column));
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        // a press on an icon button is not a press on the text: the caret stays
        _pressedIcon = e.Button == MouseButton.Left ? ClickableIconAt(e.Location) : IconPart.None;

        if (_pressedIcon != IconPart.None) return;

        // a press on a scroll bar scrolls, the caret stays (see TextBox.Scrolling)
        if (e.Button == MouseButton.Left && PressScrollBar(e.Location)) return;

        // the right and the middle button don't select: the menu they open acts on the
        // selection there is. Inside the selection it stays; outside it the caret moves
        // there, as in an editor. Any button used to move the caret, and the context
        // menu's Copy found nothing left to copy
        if (e.Button != MouseButton.Left)
        {
            // only the left button focuses by itself, and the menu's commands act on
            // the field the user pointed at
            FindOwner()?.FocusElement(this);

            int index = IndexFromPoint(e.Location);
            int start = _document.SelectionStart;

            if (_document.SelectionLength == 0 || index < start || index > start + _document.SelectionLength)
            {
                ClearComposition();
                _document.SetCaret(index);
            }

            return;
        }

        ClearComposition();

        _document.SetCaret(IndexFromPoint(e.Location));
        _isDragging = true;
    }

    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        if (MoveScrollBar(e.Location)) return;

        if (_isDragging)
        {
            _document.SetCaret(IndexFromPoint(e.Location), extendSelection: true);
            return;
        }

        // a hand over an icon that does something, the I-beam over the text
        Cursor = ClickableIconAt(e.Location) != IconPart.None ? CursorKind.Hand
            : IsOverScrollBar(e.Location) ? CursorKind.Arrow
            : CursorKind.IBeam;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        _isDragging = false;

        if (ReleaseScrollBar()) return;

        IconPart pressed = _pressedIcon;
        _pressedIcon = IconPart.None;

        if (pressed == IconPart.None || ClickableIconAt(e.Location) != pressed) return;

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

        // the caret stands inside the composition where the input method's cursor is
        int shownCaret = _document.CaretIndex + (IsComposing ? _compositionCursor : 0);

        var (caretRow, caretColumn) = IsMultiline
            ? RowOf(rows, shownCaret)
            : (0, IsComposing ? shownCaret : ToDisplayIndex(_document.CaretIndex));

        TextLineLayout caretLayout = RowLayout(rows, caretRow);
        float caretX = RowShift(caretLayout, content.Width) + caretLayout.CaretX(caretColumn);
        float caretY = caretRow * lineHeight;

        UpdateScroll(content, caretX, caretY, lineHeight, rows.Count, ContentWidth(rows),
            followCaret: TakeCaretFollow(shownCaret, rows, content));

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

            TextLineLayout layout = RowLayout(rows, i);
            float x = content.X + RowShift(layout, content.Width) - _scrollOffset;

            if (_document.SelectionLength > 0)
                DrawSelection(g, layout, row.Start, selectionStart, selectionEnd, x, y, lineHeight);

            if (row.Text.Length > 0)
            {
                // on the font's line, not the glyphs' ink: the text no longer
                // jumps up and down as letters with and without descenders are typed.
                // The backend orders it in the element's direction, as the layout did
                g.DrawTextLine(row.Text,
                    new Rectangle(new Point(x, y), new Size(float.MaxValue, lineHeight)),
                    TextColor, EffectiveFont,
                    HorizontalContentAlignment.Left, VerticalContentAlignment.Center);
            }

            if (showWhitespace)
                DrawWhitespace(g, row, layout, x, y, lineHeight);

            if (IsComposing)
                DrawCompositionUnderline(g, layout, row.Start, x, y, lineHeight);
        }

        // a hint instead of the text while the field is empty and not focused
        if (_document.Text.Length == 0 && !IsFocused && !string.IsNullOrEmpty(Watermark))
        {
            g.DrawTextLine(Watermark,
                new Rectangle(new Point(content.X, blockTop), new Size(content.Width, lineHeight)),
                WatermarkColor, EffectiveFont,
                RowAlignment, VerticalContentAlignment.Center);
        }

        var caret = new Rectangle(
            new Point(content.X + caretX - _scrollOffset, blockTop + caretY - _verticalOffset),
            new Size(CaretWidth, lineHeight));

        if (IsFocused && CaretVisible)
            g.FillRectangle(caret, CaretColor);

        // the input method's candidate list opens at the caret
        if (IsFocused)
            ReportTextInputArea(caret);

        g.Restore();

        // over the text's clip, at the edge of the text column
        DrawScrollBars(g);
    }

    /// <summary>The composition's line under its part of a row, as input methods mark the
    /// text that isn't committed yet.</summary>
    private void DrawCompositionUnderline(Graphics g, TextLineLayout layout, int rowStart, float x, float y, float lineHeight)
    {
        int start = _document.CaretIndex;
        int end = start + _composition.Length;

        int from = Math.Max(start, rowStart) - rowStart;
        int to = Math.Min(end, rowStart + layout.Text.Length) - rowStart;

        if (to <= from) return;

        float thickness = Math.Max(1f, EffectiveFont.Size / 14f);
        float underline = y + lineHeight - thickness - 1f;

        foreach ((float left, float right) in layout.SelectionBounds(from, to))
            g.FillRectangle(new Rectangle(new Point(x + left, underline), new Size(right - left, thickness)), TextColor);
    }

    /// <summary>Dots for spaces, arrows for tabs, ¶ where a line of the text ends.</summary>
    private void DrawWhitespace(Graphics g, TextRow row, TextLineLayout layout, float x, float y, float lineHeight)
    {
        Font font = EffectiveFont;
        Color color = WhitespaceColor;
        string text = row.Text;
        float middle = y + lineHeight / 2f;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (!IsSpace(c)) continue;

            (float boxLeft, float boxRight) = layout.GetCharacterBounds(i);
            float left = x + boxLeft;
            float right = x + boxRight;

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
            // where the caret at the line's end stands: the right end of a left-to-right
            // line, the left end of a right-to-left one
            float end = x + layout.CaretX(text.Length);
            float mark = TextMeasurer.MeasureLine("¶", font).Width;

            g.DrawTextLine("¶",
                new Rectangle(new Point(layout.IsRightToLeft ? end - 1f - mark : end + 1f, y), new Size(float.MaxValue, lineHeight)),
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

    /// <summary>The selection's part on a row: one span in text of one direction,
    /// several where it crosses runs of both.</summary>
    private void DrawSelection(
        Graphics g, TextLineLayout layout, int lineStartIndex,
        int selectionStart, int selectionEnd, float x, float y, float lineHeight)
    {
        int lineEndIndex = lineStartIndex + layout.Text.Length;

        int from = Math.Max(selectionStart, lineStartIndex) - lineStartIndex;
        int to = Math.Min(selectionEnd, lineEndIndex) - lineStartIndex;

        if (to <= from) return;

        foreach ((float left, float right) in layout.SelectionBounds(from, to))
        {
            g.FillRectangle(
                new Rectangle(new Point(x + left, y), new Size(right - left, lineHeight)),
                SelectionColor);
        }
    }

    /// <param name="contentWidth">The widest row: how far the text scrolls sideways.</param>
    /// <param name="followCaret">Bring the caret into view: it moved, the text changed or
    /// the field was resized. Otherwise the field stays where the wheel or a bar put it,
    /// kept within the text.</param>
    private void UpdateScroll(Rectangle content, float caretX, float caretY, float lineHeight, int lineCount,
        float contentWidth, bool followCaret)
    {
        // wrapped rows fit the width: nothing to scroll sideways to. Nor in a single
        // line that fits — it stands where its alignment puts it, and an offset left
        // from when it was longer used to keep its start hidden
        if ((IsMultiline && WordWrap) || (!IsMultiline && contentWidth <= content.Width))
        {
            _scrollOffset = 0;
        }
        else
        {
            if (followCaret)
            {
                if (caretX - _scrollOffset > content.Width) _scrollOffset = caretX - content.Width;
                else if (caretX - _scrollOffset < 0) _scrollOffset = caretX;
            }

            // the caret stands after the last character: room for it at the end
            _scrollOffset = Math.Clamp(_scrollOffset, 0, Math.Max(0, Math.Max(contentWidth, caretX + CaretWidth) - content.Width));
        }

        if (!IsMultiline)
        {
            _verticalOffset = 0;
            return;
        }

        if (followCaret)
        {
            if (caretY + lineHeight - _verticalOffset > content.Height)
                _verticalOffset = caretY + lineHeight - content.Height;
            else if (caretY - _verticalOffset < 0)
                _verticalOffset = caretY;
        }

        // don't scroll below the last line — or, with ScrollPastEnd, below the last
        // line standing at the top
        _verticalOffset = Math.Clamp(_verticalOffset, 0,
            Math.Max(0, lineCount * lineHeight - content.Height) + PastEndExtent(content));
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