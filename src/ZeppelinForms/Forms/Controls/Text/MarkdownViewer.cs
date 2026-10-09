using ZeppelinForms.Core.Markdown;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls.Text;

/// <summary>A link in a <see cref="MarkdownViewer"/> was followed.</summary>
public sealed class MarkdownLinkEventArgs(string url, string? title, string text) : EventArgs
{
    public string Url { get; } = url;

    public string? Title { get; } = title;

    /// <summary>The link's text as it reads.</summary>
    public string Text { get; } = text;

    /// <summary>The application took care of it: a <c>#fragment</c> link is then
    /// not scrolled to by the viewer.</summary>
    public bool Handled { get; set; }
}

/// <summary>
/// Shows a Markdown document — a README, release notes, help, a chat message —
/// wrapped to the width and scrolling when it is longer than the control.
/// </summary>
/// <remarks>
/// <para>
/// Headings, paragraphs with emphasis and code, quotes, lists with task boxes,
/// code blocks, tables, rules and pictures. A link raises <see cref="LinkClicked"/>;
/// one to <c>#a-heading</c> that nobody handles scrolls to that heading.
/// Pictures come from <see cref="ImageResolver"/>; without one, or while it has
/// nothing yet, a picture reads as its alt text.
/// </para>
/// <para>
/// From the keyboard Tab goes through the links and Enter follows one, the arrows
/// and Page Up/Down scroll. A screen reader gets the document's structure — headings
/// with their levels, lists, tables, links — not just its lines.
/// </para>
/// </remarks>
public partial class MarkdownViewer : DecoratedPanel, IInputElement
{
    /// <summary>The width a document is laid out at when nothing limits it: a
    /// comfortable line, not one line per paragraph.</summary>
    private const float UnboundedWidth = 680f;

    private MarkdownDocument _document = MarkdownDocument.Empty;
    private MarkdownLayout? _layout;

    // what the layout was built for: rebuilt when any of it changes
    private float _layoutWidth = -1f;
    private Font? _layoutFont;
    private bool _layoutRtl;
    private bool _layoutStale = true;

    private int _hoveredLink = -1;
    private int _focusedLink = -1;

    public MarkdownViewer()
    {
        OverflowY = Overflow.Auto;
    }

    public bool IsFocused { get; set; }
    public bool TabStop { get; set; } = true;
    public uint TabIndex { get; set; }

    // ===== content =====

    /// <summary>The Markdown source.</summary>
    public string? Markdown
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            _document = MarkdownDocument.Parse(value);

            _hoveredLink = _focusedLink = -1;
            ScrollTo(0, 0);

            RefreshLayout();
        }
    }

    /// <summary>The parsed document.</summary>
    public MarkdownDocument Document => _document;

    /// <summary>Pictures by their URL: <c>![logo](logo.png)</c> asks for "logo.png".
    /// Null — the picture reads as its alt text. A picture that arrives later is
    /// shown after <see cref="RefreshLayout"/>.</summary>
    public Func<string, Image?>? ImageResolver
    {
        get;
        set
        {
            field = value;
            RefreshLayout();
        }
    }

    /// <summary>The font of code; the system monospace at the text's size when null.</summary>
    public Font? CodeFont
    {
        get;
        set
        {
            field = value;
            RefreshLayout();
        }
    }

    /// <summary>A link was followed: clicked, Enter, or by a screen reader.</summary>
    public event EventHandler<MarkdownLinkEventArgs>? LinkClicked;

    /// <summary>Lay the document out again: a picture the resolver has now.</summary>
    public void RefreshLayout()
    {
        _layoutStale = true;
        LayoutChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    /// <summary>The layout was dropped, for the peer: the structure it reads may change.</summary>
    internal event EventHandler? LayoutChanged;

    internal MarkdownLayout? Layout => _layout;

    internal int FocusedLink => IsFocused ? _focusedLink : -1;

    // ===== look =====

    /// <summary>The spacing of lines, as a multiple of the font's height.</summary>
    [Styled(Category = "Text", AffectsLayout = true)]
    public partial float LineSpacing { get; set; }
    private static float LineSpacingDefault => 1.3f;

    [Styled(Category = "Text")]
    public partial Color HeadingColor { get; set; }
    private static Color HeadingColorDefault => Colors.Transparent;

    [Styled(Category = "Links")]
    public partial Color LinkColor { get; set; }
    private static Color LinkColorDefault => new(255, 0x0D, 0x6E, 0xFD);

    [Styled(Category = "Links")]
    public partial Color LinkHoverColor { get; set; }
    private static Color LinkHoverColorDefault => new(255, 0x0A, 0x58, 0xCA);

    /// <summary>The ring around the link the keyboard is on.</summary>
    [Styled(Category = "Links")]
    public partial Color FocusColor { get; set; }
    private static Color FocusColorDefault => new(255, 26, 26, 26);

    /// <summary>The text of code; transparent — the text color.</summary>
    [Styled(Category = "Code")]
    public partial Color CodeTextColor { get; set; }
    private static Color CodeTextColorDefault => Colors.Transparent;

    /// <summary>Behind <c>`code`</c> in a line.</summary>
    [Styled(Category = "Code")]
    public partial Color CodeBackground { get; set; }
    private static Color CodeBackgroundDefault => new(255, 239, 241, 243);

    /// <summary>Behind a code block.</summary>
    [Styled(Category = "Code")]
    public partial Color CodeBlockBackground { get; set; }
    private static Color CodeBlockBackgroundDefault => new(255, 246, 248, 250);

    [Styled(Category = "Quote")]
    public partial Color QuoteBarColor { get; set; }
    private static Color QuoteBarColorDefault => new(255, 208, 215, 222);

    /// <summary>The text of a quote; transparent — the text color.</summary>
    [Styled(Category = "Quote")]
    public partial Color QuoteTextColor { get; set; }
    private static Color QuoteTextColorDefault => new(255, 89, 99, 110);

    /// <summary>Rules, and the line under the two top heading levels.</summary>
    [Styled(Category = "Text")]
    public partial Color RuleColor { get; set; }
    private static Color RuleColorDefault => new(255, 216, 222, 228);

    [Styled(Category = "Table")]
    public partial Color TableBorderColor { get; set; }
    private static Color TableBorderColorDefault => new(255, 208, 215, 222);

    [Styled(Category = "Table")]
    public partial Color TableHeaderBackground { get; set; }
    private static Color TableHeaderBackgroundDefault => new(255, 246, 248, 250);

    /// <summary>Every second body row; transparent — no stripes.</summary>
    [Styled(Category = "Table")]
    public partial Color TableStripeColor { get; set; }
    private static Color TableStripeColorDefault => new(255, 250, 251, 252);

    /// <summary>The boxes of task items: a done one is filled with it.</summary>
    [Styled(Category = "Lists")]
    public partial Color TaskColor { get; set; }
    private static Color TaskColorDefault => new(255, 0, 120, 215);

    /// <summary>The check mark in a done task box.</summary>
    [Styled(Category = "Lists")]
    public partial Color TaskCheckColor { get; set; }
    private static Color TaskCheckColorDefault => Colors.White;

    /// <summary>The viewer's own look is baked into the layout — the colors of links,
    /// code, quotes: a change of it lays the document out again.</summary>
    protected override void OnStyledPropertyChanged(StyledProperty property)
    {
        base.OnStyledPropertyChanged(property);

        if (property.OwnerType != typeof(MarkdownViewer)) return;

        _layoutStale = true;

        // a layout property requests the pass itself
        if (!property.AffectsLayout) Invalidate();
    }

    // ===== layout =====

    private MarkdownLayout EnsureLayout(float width)
    {
        Font font = EffectiveFont;
        bool rtl = IsRightToLeft;

        if (_layout is null || _layoutStale || _layoutWidth != width || _layoutFont != font || _layoutRtl != rtl)
        {
            bool rebuilt = _layout is not null;

            _layout = MarkdownLayouter.Build(_document, this, font, width);
            _layoutWidth = width;
            _layoutFont = font;
            _layoutRtl = rtl;
            _layoutStale = false;

            if (_focusedLink >= _layout.Links.Count) _focusedLink = -1;
            if (_hoveredLink >= _layout.Links.Count) _hoveredLink = -1;

            if (rebuilt) LayoutChanged?.Invoke(this, EventArgs.Empty);
        }

        return _layout;
    }

    protected override Size MeasureContentOverride(Size availableSize)
    {
        bool bounded = float.IsFinite(availableSize.Width);
        float width = bounded ? Math.Max(1f, availableSize.Width - Padding.Horizontal) : UnboundedWidth;

        MarkdownLayout layout = EnsureLayout(width);

        return new Size(
            bounded ? availableSize.Width : layout.Width + Padding.Horizontal,
            layout.Height + Padding.Vertical);
    }

    protected override void ArrangeContentOverride(Size contentSize) =>
        EnsureLayout(Math.Max(1f, contentSize.Width - Padding.Horizontal));

    /// <summary>A point of the viewer in the layout's coordinates.</summary>
    private Point ToContent(Point local) =>
        new(local.X - Padding.Left + ScrollX, local.Y - Padding.Top + ScrollY);

    /// <summary>A rectangle of the layout in the viewer's coordinates, as scrolled now.</summary>
    internal Rectangle FromContent(Rectangle rect) =>
        new(new Point(rect.X + Padding.Left - ScrollX, rect.Y + Padding.Top - ScrollY), rect.Size);

    // ===== drawing =====

    protected override void DrawContent(Graphics g)
    {
        if (_layout is not { } layout) return;

        float top = ScrollY - Padding.Top;
        float bottom = top + ActualSize.Height;

        g.Save();
        g.ClipRect(Viewport);
        g.Translate(Padding.Left - ScrollX, Padding.Top - ScrollY);

        Color text = TextColor;

        foreach (MarkdownBox box in layout.Boxes)
        {
            if (box.Bounds.Y > bottom || box.Bounds.Y + box.Bounds.Height < top) continue;

            DrawBox(g, box, box.Color.A > 0 ? box.Color : text);
        }

        int highlighted = _hoveredLink >= 0 ? _hoveredLink : FocusedLink;

        foreach (MarkdownLine line in layout.Lines)
        {
            if (line.Bounds.Y > bottom || line.Bounds.Y + line.Bounds.Height < top) continue;

            TextRun[] runs = line.Runs;

            // the link under the pointer or the keyboard takes its own color
            if (highlighted >= 0 && Array.IndexOf(line.RunLinks, highlighted) >= 0)
            {
                runs = [.. runs];

                for (int i = 0; i < runs.Length; i++)
                    if (line.RunLinks[i] == highlighted)
                        runs[i] = runs[i] with { Color = LinkHoverColor };
            }

            g.DrawRuns(runs, line.Bounds, EffectiveFont, text, line.Align, VerticalContentAlignment.Center);
        }

        if (FocusedLink is var focused and >= 0 && IsFocusVisible && FocusColor.A > 0)
        {
            foreach (Rectangle rect in layout.Links[focused].Rects)
            {
                var ring = new Rectangle(
                    new Point(rect.X - 2f, rect.Y),
                    new Size(rect.Width + 4f, rect.Height));

                g.DrawRoundRectangle(ring, new CornerRadius(3f), FocusColor, 2f);
            }
        }

        g.Restore();
    }

    private void DrawBox(Graphics g, MarkdownBox box, Color color)
    {
        Rectangle r = box.Bounds;

        switch (box.Kind)
        {
            case MarkdownBoxKind.Fill when box.Radius > 0f:
                g.FillRoundRectangle(r, new CornerRadius(box.Radius), color);
                break;

            case MarkdownBoxKind.Fill:
                g.FillRectangle(r, color);
                break;

            case MarkdownBoxKind.Stroke:
                g.DrawRoundRectangle(r, new CornerRadius(box.Radius), color, 1f);
                break;

            case MarkdownBoxKind.Disc:
                g.FillEllipse(r, color);
                break;

            case MarkdownBoxKind.Circle:
                g.DrawEllipse(r, color, 1.2f);
                break;

            case MarkdownBoxKind.Square:
                g.FillRectangle(r, color);
                break;

            case MarkdownBoxKind.Check:
                if (box.IsChecked)
                {
                    g.FillRoundRectangle(r, new CornerRadius(box.Radius), color);

                    float cx = r.X + r.Width / 2f;
                    float cy = r.Y + r.Height / 2f;
                    float s = r.Width * 0.28f;

                    g.DrawPolyline(
                        [new(cx - s, cy), new(cx - s * 0.25f, cy + s * 0.75f), new(cx + s, cy - s * 0.7f)],
                        TaskCheckColor, Math.Max(1.5f, r.Width / 9f));
                }
                else
                {
                    g.DrawRoundRectangle(r, new CornerRadius(box.Radius), color, 1.5f);
                }

                break;

            case MarkdownBoxKind.Image when box.Image is { } image:
                g.DrawImage(r, image, ImageFlip.None, ImageLayout.Stretch);
                break;
        }
    }

    // ===== links =====

    /// <summary>Follow a link: <see cref="LinkClicked"/>, and a <c>#heading</c>
    /// link nobody handled scrolls there.</summary>
    internal void Activate(int link)
    {
        if (_layout is null || link < 0 || link >= _layout.Links.Count) return;

        MarkdownLink target = _layout.Links[link];
        var args = new MarkdownLinkEventArgs(target.Url, target.Title, target.Text);

        LinkClicked?.Invoke(this, args);

        if (!args.Handled && target.Url.StartsWith('#'))
            ScrollToAnchor(target.Url[1..]);
    }

    /// <summary>The anchors of the headings, as <c>#links</c> name them.</summary>
    public IReadOnlyCollection<string> Anchors => (IReadOnlyCollection<string>?)_layout?.Anchors.Keys ?? [];

    /// <summary>Scroll so that the heading with this anchor is at the top.
    /// False — there is no such heading.</summary>
    public bool ScrollToAnchor(string anchor)
    {
        FindOwner()?.UpdateLayout();

        if (_layout is null) return false;

        string key = Uri.UnescapeDataString(anchor).Trim().ToLowerInvariant();

        if (!_layout.Anchors.TryGetValue(key, out float y)
            && !_layout.Anchors.TryGetValue(MarkdownDocument.Slug(key), out y))
            return false;

        ScrollTo(ScrollX, y + Padding.Top - Viewport.Y);
        return true;
    }

    /// <summary>Scroll so that a link is in view whole.</summary>
    private void RevealLink(int link)
    {
        if (_layout is null || link < 0 || _layout.Links[link].Rects.Count == 0) return;

        Rectangle first = _layout.Links[link].Rects[0];
        Rectangle view = Viewport;

        float top = first.Y + Padding.Top;
        float bottom = top + first.Height;

        if (top - ScrollY < view.Y) ScrollTo(ScrollX, top - view.Y);
        else if (bottom - ScrollY > view.Y + view.Height) ScrollTo(ScrollX, bottom - view.Y - view.Height);
    }

    private int LinkAtLocal(Point local) =>
        _layout is null || HitTestSelfFirst(local) ? -1 : _layout.LinkAt(ToContent(local));

    private Point ToLocal(Point absolute)
    {
        Point origin = GetAbsolutePosition();

        return new Point(absolute.X - origin.X, absolute.Y - origin.Y);
    }

    // ===== mouse =====

    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        base.OnMouseMove(e);

        int link = LinkAtLocal(ToLocal(e.Location));

        if (link == _hoveredLink) return;

        _hoveredLink = link;
        Cursor = link >= 0 ? CursorKind.Hand : CursorKind.Default;

        RefreshToolTip();
        InvalidateVisual();
    }

    protected override void OnMouseExit(MouseMoveEventArgs e)
    {
        base.OnMouseExit(e);

        if (_hoveredLink < 0) return;

        _hoveredLink = -1;
        Cursor = CursorKind.Default;

        InvalidateVisual();
    }

    protected override void OnClick(MouseClickEventArgs e)
    {
        if (e.Button != MouseButton.Left) return;

        int link = LinkAtLocal(ToLocal(e.Location));

        if (link < 0) return;

        Activate(link);
        e.Handled = true;
    }

    /// <summary>A link's title, or where it goes.</summary>
    protected internal override string? GetToolTip(Point location)
    {
        int link = LinkAtLocal(ToLocal(location));

        if (link < 0 || _layout is null) return ToolTip;

        MarkdownLink target = _layout.Links[link];

        return target.Title ?? target.Url;
    }

    // ===== keyboard =====

    protected override void OnGotFocus()
    {
        base.OnGotFocus();
        InvalidateVisual();
    }

    protected override void OnLostFocus()
    {
        base.OnLostFocus();

        _focusedLink = -1;
        InvalidateVisual();
    }

    private void FocusLink(int link)
    {
        _focusedLink = link;

        RevealLink(link);
        InvalidateVisual();

        FocusedLinkChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The keyboard moved to another link, for the peer.</summary>
    internal event EventHandler? FocusedLinkChanged;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        int links = _layout?.Links.Count ?? 0;
        float line = TextMeasurer.Current.MeasureText("Wg", EffectiveFont).Height * LineSpacing;
        float page = Math.Max(line, Viewport.Height - line);

        switch (e.Key)
        {
            // Tab goes through the links, then on out of the viewer
            case Key.Tab when links > 0 && !e.Modifiers.HasFlag(KeyModifiers.Control):
                if (e.Modifiers.HasFlag(KeyModifiers.Shift))
                {
                    if (_focusedLink < 0) break;

                    FocusLink(_focusedLink - 1);
                }
                else
                {
                    if (_focusedLink >= links - 1)
                    {
                        _focusedLink = -1;
                        InvalidateVisual();
                        break;
                    }

                    FocusLink(_focusedLink + 1);
                }

                e.Handled = true;
                return;

            case Key.Enter when _focusedLink >= 0:
                Activate(_focusedLink);
                e.Handled = true;
                return;

            case Key.Up:
                ScrollTo(ScrollX, ScrollY - line);
                e.Handled = true;
                return;

            case Key.Down:
                ScrollTo(ScrollX, ScrollY + line);
                e.Handled = true;
                return;

            case Key.PageUp:
                ScrollTo(ScrollX, ScrollY - page);
                e.Handled = true;
                return;

            case Key.PageDown or Key.Space:
                ScrollTo(ScrollX, ScrollY + (e.Modifiers.HasFlag(KeyModifiers.Shift) ? -page : page));
                e.Handled = true;
                return;

            case Key.Home when e.Modifiers.HasFlag(KeyModifiers.Control) || e.Modifiers == KeyModifiers.None:
                ScrollTo(ScrollX, 0);
                e.Handled = true;
                return;

            case Key.End when e.Modifiers.HasFlag(KeyModifiers.Control) || e.Modifiers == KeyModifiers.None:
                ScrollTo(ScrollX, float.MaxValue);
                e.Handled = true;
                return;
        }

        base.OnKeyDown(e);
    }
}