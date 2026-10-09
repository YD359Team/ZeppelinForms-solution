using System.Globalization;
using ZeppelinForms.Accessibility;
using ZeppelinForms.Core.Markdown;
using ZeppelinForms.Core.Text;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms.Controls.Text;

/// <summary>A line of text, ready to draw: its runs share a baseline.</summary>
/// <param name="RunLinks">The link each run belongs to, −1 for none: a hovered or
/// focused link is drawn in its own color.</param>
internal sealed record MarkdownLine(Rectangle Bounds, TextRun[] Runs, int[] RunLinks, HorizontalContentAlignment Align);

internal enum MarkdownBoxKind : byte { Fill, Stroke, Disc, Circle, Square, Check, Image }

/// <summary>What is drawn besides text: backgrounds, bars, rules, bullets, boxes, pictures.
/// A transparent color is the viewer's text color, taken when drawing: a bullet
/// follows the text it stands by, inherited color included.</summary>
internal sealed record MarkdownBox(
    MarkdownBoxKind Kind, Rectangle Bounds, Color Color,
    float Radius = 0f, bool IsChecked = false, Image? Image = null, int Link = -1);

/// <summary>A link of the document: where it goes, and where it is on screen —
/// a link that wraps is in more than one rectangle.</summary>
internal sealed class MarkdownLink(string url, string? title, string text)
{
    public string Url { get; } = url;
    public string? Title { get; } = title;
    public string Text { get; } = text;
    public List<Rectangle> Rects { get; } = [];
}

/// <summary>A node of the tree a screen reader walks: a heading, a paragraph, a
/// list item, a link — the document's structure, not its lines.</summary>
internal sealed class MarkdownNode(AccessibilityRole role, string name)
{
    public AccessibilityRole Role { get; } = role;
    public string Name { get; set; } = name;
    public int Level { get; init; }
    public int Link { get; init; } = -1;
    public bool? IsChecked { get; init; }
    public Rectangle Bounds { get; set; }
    public List<MarkdownNode> Children { get; } = [];
}

/// <summary>A document laid out at one width, in content coordinates.</summary>
internal sealed class MarkdownLayout
{
    public List<MarkdownLine> Lines { get; } = [];
    public List<MarkdownBox> Boxes { get; } = [];
    public List<MarkdownLink> Links { get; } = [];
    public Dictionary<string, float> Anchors { get; } = new(StringComparer.Ordinal);
    public MarkdownNode Root { get; } = new(AccessibilityRole.None, string.Empty);

    public float Width { get; set; }
    public float Height { get; set; }

    /// <summary>The link under a point, −1 for none.</summary>
    public int LinkAt(Point point)
    {
        for (int i = 0; i < Links.Count; i++)
            foreach (Rectangle rect in Links[i].Rects)
                if (point.X >= rect.X && point.X <= rect.X + rect.Width &&
                    point.Y >= rect.Y && point.Y <= rect.Y + rect.Height)
                    return i;

        return -1;
    }
}

/// <summary>Lays a <see cref="MarkdownDocument"/> out for a <see cref="MarkdownViewer"/>.</summary>
internal sealed class MarkdownLayouter
{
    /// <summary>The look of a piece of inline text.</summary>
    private readonly record struct Style(
        Font Font, Color? Color, Color? Background, bool Underline, bool Strike, int Link);

    /// <summary>The unit of wrapping: a word, the spaces after it, or a forced break.</summary>
    private readonly record struct Atom(string Text, Style Style, float Width, bool IsSpace, bool IsBreak);

    /// <summary>What a block's contents inherit: the text color inside a quote,
    /// how deep in lists it is.</summary>
    private readonly record struct Context(Color? TextColor, int ListDepth);

    private static readonly float[] HeadingScale = [2.0f, 1.5f, 1.25f, 1.0f, 0.875f, 0.85f];

    private readonly MarkdownViewer _viewer;
    private readonly MarkdownLayout _layout = new();
    private readonly Font _font;
    private readonly Font _codeFont;
    private readonly float _gap;
    private readonly HorizontalContentAlignment _align;

    private MarkdownLayouter(MarkdownViewer viewer, Font font)
    {
        _viewer = viewer;
        _font = font;
        _codeFont = viewer.CodeFont ?? Font.Monospace.WithSize(font.Size * 0.9f);
        _gap = font.Size * 0.8f;
        _align = viewer.IsRightToLeft ? HorizontalContentAlignment.Right : HorizontalContentAlignment.Left;
    }

    public static MarkdownLayout Build(MarkdownDocument document, MarkdownViewer viewer, Font font, float width)
    {
        var layouter = new MarkdownLayouter(viewer, font);

        MarkdownLayout layout = layouter._layout;
        layout.Width = width;
        layout.Height = layouter.LayoutBlocks(document.Blocks, 0f, 0f, width, new Context(null, 0), layout.Root, tight: false);

        return layout;
    }

    private static float Measure(string text, Font font) => TextMeasurer.Current.MeasureText(text, font).Width;

    private float LineHeight(Font font) =>
        TextMeasurer.Current.MeasureText("Wg", font).Height * Math.Max(1f, _viewer.LineSpacing);

    // ===== blocks =====

    private float LayoutBlocks(IReadOnlyList<MarkdownBlock> blocks, float x, float y, float width, Context context, MarkdownNode parent, bool tight)
    {
        for (int i = 0; i < blocks.Count; i++)
        {
            if (i > 0)
                y += tight ? _font.Size * 0.25f : blocks[i] is HeadingBlock ? _gap * 1.5f : _gap;

            y = LayoutBlock(blocks[i], x, y, width, context, parent, tight);
        }

        return y;
    }

    private float LayoutBlock(MarkdownBlock block, float x, float y, float width, Context context, MarkdownNode parent, bool tight) => block switch
    {
        HeadingBlock heading => LayoutHeading(heading, x, y, width, context, parent),
        ParagraphBlock paragraph => LayoutParagraph(paragraph, x, y, width, context, parent),
        CodeBlock code => LayoutCode(code, x, y, width, parent),
        QuoteBlock quote => LayoutQuote(quote, x, y, width, context, parent),
        ListBlock list => LayoutList(list, x, y, width, context, parent),
        RuleBlock => LayoutRule(x, y, width, parent),
        TableBlock table => LayoutTable(table, x, y, width, context, parent),
        _ => y,
    };

    private float LayoutHeading(HeadingBlock heading, float x, float y, float width, Context context, MarkdownNode parent)
    {
        float scale = HeadingScale[Math.Clamp(heading.Level, 1, 6) - 1];
        Font font = _font.WithSize(_font.Size * scale).Bold();

        Color? color = _viewer.HeadingColor.A > 0 ? _viewer.HeadingColor : context.TextColor;

        _layout.Anchors.TryAdd(heading.Anchor, y);

        var node = new MarkdownNode(AccessibilityRole.Heading, MarkdownInline.PlainText(heading.Inlines))
        {
            Level = heading.Level,
        };

        float top = y;
        y = LayoutInlines(heading.Inlines, x, y, width, new Style(font, color, null, false, false, -1), _align, node);

        // the two top levels are underlined, as on GitHub
        if (heading.Level <= 2 && _viewer.RuleColor.A > 0)
        {
            y += font.Size * 0.25f;
            _layout.Boxes.Add(new MarkdownBox(MarkdownBoxKind.Fill, new Rectangle(new Point(x, y), new Size(width, 1f)), _viewer.RuleColor));
            y += 1f;
        }

        node.Bounds = new Rectangle(new Point(x, top), new Size(width, y - top));
        parent.Children.Add(node);

        return y;
    }

    private float LayoutParagraph(ParagraphBlock paragraph, float x, float y, float width, Context context, MarkdownNode parent)
    {
        // a paragraph that is one picture — a screenshot, a diagram — is a picture
        if (SoleImage(paragraph.Inlines, out ImageInline? image, out LinkInline? around)
            && _viewer.ImageResolver?.Invoke(image!.Url) is { } picture)
        {
            return LayoutImage(picture, image, around, x, y, width, parent);
        }

        var node = new MarkdownNode(AccessibilityRole.Text, MarkdownInline.PlainText(paragraph.Inlines));
        float top = y;

        y = LayoutInlines(paragraph.Inlines, x, y, width, new Style(_font, context.TextColor, null, false, false, -1), _align, node);

        node.Bounds = new Rectangle(new Point(x, top), new Size(width, y - top));
        parent.Children.Add(node);

        return y;
    }

    private static bool SoleImage(IReadOnlyList<MarkdownInline> inlines, out ImageInline? image, out LinkInline? link)
    {
        image = null;
        link = null;

        if (inlines.Count != 1) return false;

        switch (inlines[0])
        {
            case ImageInline alone:
                image = alone;
                return true;

            case LinkInline { Children: [ImageInline inner] } wrapping:
                image = inner;
                link = wrapping;
                return true;

            default:
                return false;
        }
    }

    private float LayoutImage(Image picture, ImageInline image, LinkInline? around, float x, float y, float width, MarkdownNode parent)
    {
        // never wider than the column, never larger than the picture itself
        float scale = Math.Min(1f, width / Math.Max(1, picture.Width));
        var size = new Size(picture.Width * scale, picture.Height * scale);

        float left = _align == HorizontalContentAlignment.Right ? x + width - size.Width : x;
        var bounds = new Rectangle(new Point(left, y), size);

        int link = -1;

        if (around is not null)
        {
            link = AddLink(around.Url, around.Title, image.Alt);
            _layout.Links[link].Rects.Add(bounds);
        }

        _layout.Boxes.Add(new MarkdownBox(MarkdownBoxKind.Image, bounds, default, Image: picture, Link: link));

        var node = new MarkdownNode(AccessibilityRole.Image, image.Alt.Length > 0 ? image.Alt : image.Title ?? string.Empty)
        {
            Bounds = bounds,
        };

        if (link >= 0)
        {
            var linkNode = new MarkdownNode(AccessibilityRole.Link, image.Alt) { Link = link, Bounds = bounds };
            linkNode.Children.Add(node);
            parent.Children.Add(linkNode);
        }
        else
        {
            parent.Children.Add(node);
        }

        return y + size.Height;
    }

    private float LayoutCode(CodeBlock code, float x, float y, float width, MarkdownNode parent)
    {
        float pad = _font.Size * 0.85f;
        float lineHeight = LineHeight(_codeFont);
        float inner = Math.Max(1f, width - pad * 2f);
        float top = y;

        // the background goes in first: the boxes are drawn under the lines
        int backgroundIndex = _layout.Boxes.Count;
        _layout.Boxes.Add(new MarkdownBox(MarkdownBoxKind.Fill, default, _viewer.CodeBlockBackground, _font.Size * 0.4f));

        y += pad;

        Color? color = _viewer.CodeTextColor.A > 0 ? _viewer.CodeTextColor : null;

        foreach (string source in code.Code.Split('\n'))
        {
            // code keeps its indentation and wraps only where a line doesn't fit
            foreach (string piece in BreakToWidth(source.Replace("\t", "    "), _codeFont, inner))
            {
                var bounds = new Rectangle(new Point(x + pad, y), new Size(inner, lineHeight));

                if (piece.Length > 0)
                    _layout.Lines.Add(new MarkdownLine(bounds, [new TextRun(piece) { Font = _codeFont, Color = color }], [-1], HorizontalContentAlignment.Left));

                y += lineHeight;
            }
        }

        y += pad;

        _layout.Boxes[backgroundIndex] = _layout.Boxes[backgroundIndex] with
        {
            Bounds = new Rectangle(new Point(x, top), new Size(width, y - top)),
        };

        parent.Children.Add(new MarkdownNode(AccessibilityRole.Text, code.Code)
        {
            Bounds = new Rectangle(new Point(x, top), new Size(width, y - top)),
        });

        return y;
    }

    /// <summary>A line of preformatted text cut into pieces that fit, at grapheme
    /// boundaries. An empty line stays one empty piece.</summary>
    private static List<string> BreakToWidth(string line, Font font, float width)
    {
        var pieces = new List<string>();

        if (line.Length == 0 || Measure(line, font) <= width)
        {
            pieces.Add(line);
            return pieces;
        }

        int start = 0;

        while (start < line.Length)
        {
            int end = start;
            int next = TextElements.Next(line, end);

            // at least one element per piece, however narrow the column
            end = next;

            while (end < line.Length)
            {
                next = TextElements.Next(line, end);

                if (Measure(line[start..next], font) > width) break;

                end = next;
            }

            pieces.Add(line[start..end]);
            start = end;
        }

        return pieces;
    }

    private float LayoutQuote(QuoteBlock quote, float x, float y, float width, Context context, MarkdownNode parent)
    {
        const float bar = 3f;
        float indent = bar + _font.Size * 0.85f;

        bool rtl = _align == HorizontalContentAlignment.Right;
        float top = y;

        var node = new MarkdownNode(AccessibilityRole.Group, string.Empty);

        Color? color = _viewer.QuoteTextColor.A > 0 ? _viewer.QuoteTextColor : context.TextColor;

        y = LayoutBlocks(quote.Blocks, rtl ? x : x + indent, y, Math.Max(1f, width - indent), context with { TextColor = color }, node, tight: false);

        _layout.Boxes.Add(new MarkdownBox(MarkdownBoxKind.Fill,
            new Rectangle(new Point(rtl ? x + width - bar : x, top), new Size(bar, y - top)),
            _viewer.QuoteBarColor, bar / 2f));

        node.Bounds = new Rectangle(new Point(x, top), new Size(width, y - top));
        parent.Children.Add(node);

        return y;
    }

    private float LayoutList(ListBlock list, float x, float y, float width, Context context, MarkdownNode parent)
    {
        bool rtl = _align == HorizontalContentAlignment.Right;
        bool tasks = list.Items.Any(item => item.IsChecked is not null);

        int last = list.Start + list.Items.Count - 1;

        float markerWidth = list.IsOrdered
            ? Measure(last.ToString(CultureInfo.InvariantCulture) + ".", _font) + _font.Size * 0.6f
            : _font.Size * 1.3f;

        if (tasks) markerWidth = Math.Max(markerWidth, _font.Size * 1.6f);

        float contentX = rtl ? x : x + markerWidth;
        float contentWidth = Math.Max(1f, width - markerWidth);

        var listNode = new MarkdownNode(AccessibilityRole.List, string.Empty);
        float listTop = y;

        for (int i = 0; i < list.Items.Count; i++)
        {
            ListItemBlock item = list.Items[i];

            if (i > 0) y += list.IsLoose ? _gap : _font.Size * 0.25f;

            int firstLine = _layout.Lines.Count;
            float top = y;

            string name = item.Blocks.Count > 0 && item.Blocks[0] is ParagraphBlock first
                ? MarkdownInline.PlainText(first.Inlines)
                : string.Empty;

            var itemNode = new MarkdownNode(AccessibilityRole.ListItem, name) { IsChecked = item.IsChecked };

            y = LayoutBlocks(item.Blocks, contentX, y, contentWidth, context with { ListDepth = context.ListDepth + 1 }, itemNode, tight: !list.IsLoose);

            // an empty item still takes a line, for its marker
            if (y <= top) y = top + LineHeight(_font);

            // the marker stands at the item's first line
            Rectangle firstBounds = firstLine < _layout.Lines.Count
                ? _layout.Lines[firstLine].Bounds
                : new Rectangle(new Point(contentX, top), new Size(contentWidth, LineHeight(_font)));

            var markerColumn = new Rectangle(
                new Point(rtl ? x + width - markerWidth : x, firstBounds.Y),
                new Size(markerWidth, firstBounds.Height));

            AddMarker(list, item, i, markerColumn, context, rtl);

            itemNode.Bounds = new Rectangle(new Point(x, top), new Size(width, y - top));
            listNode.Children.Add(itemNode);
        }

        listNode.Bounds = new Rectangle(new Point(x, listTop), new Size(width, y - listTop));
        parent.Children.Add(listNode);

        return y;
    }

    private void AddMarker(ListBlock list, ListItemBlock item, int index, Rectangle column, Context context, bool rtl)
    {
        float centerY = column.Y + column.Height / 2f;

        if (item.IsChecked is { } done)
        {
            float size = _font.Size * 0.95f;
            float left = rtl ? column.X + column.Width - size : column.X;

            _layout.Boxes.Add(new MarkdownBox(MarkdownBoxKind.Check,
                new Rectangle(new Point(left, centerY - size / 2f), new Size(size, size)),
                _viewer.TaskColor, _font.Size * 0.15f, IsChecked: done));

            return;
        }

        if (list.IsOrdered)
        {
            string number = (list.Start + index).ToString(CultureInfo.CurrentCulture) + ".";

            // the numbers end at the same place, a little before the text
            var bounds = new Rectangle(
                new Point(rtl ? column.X + _font.Size * 0.4f : column.X, column.Y),
                new Size(column.Width - _font.Size * 0.4f, column.Height));

            _layout.Lines.Add(new MarkdownLine(bounds,
                [new TextRun(number) { Font = _font, Color = context.TextColor }], [-1],
                rtl ? HorizontalContentAlignment.Left : HorizontalContentAlignment.Right));

            return;
        }

        // a disc, a circle, a square — deeper lists tell themselves apart
        MarkdownBoxKind kind = (context.ListDepth % 3) switch
        {
            0 => MarkdownBoxKind.Disc,
            1 => MarkdownBoxKind.Circle,
            _ => MarkdownBoxKind.Square,
        };

        float diameter = _font.Size * 0.35f;
        float centerX = column.X + column.Width / 2f;

        _layout.Boxes.Add(new MarkdownBox(kind,
            new Rectangle(new Point(centerX - diameter / 2f, centerY - diameter / 2f), new Size(diameter, diameter)),
            context.TextColor ?? default));
    }

    private float LayoutRule(float x, float y, float width, MarkdownNode parent)
    {
        y += _font.Size * 0.25f;

        var bounds = new Rectangle(new Point(x, y), new Size(width, 2f));
        _layout.Boxes.Add(new MarkdownBox(MarkdownBoxKind.Fill, bounds, _viewer.RuleColor, 1f));

        parent.Children.Add(new MarkdownNode(AccessibilityRole.Separator, string.Empty) { Bounds = bounds });

        return y + 2f + _font.Size * 0.25f;
    }

    private float LayoutTable(TableBlock table, float x, float y, float width, Context context, MarkdownNode parent)
    {
        int columns = table.Alignments.Count;
        float padX = _font.Size * 0.6f;
        float padY = _font.Size * 0.35f;

        Font headerFont = _font.Bold();

        // what each column would like, and the least it can live with: its longest word
        var natural = new float[columns];
        var minimum = new float[columns];

        void Consider(IReadOnlyList<IReadOnlyList<MarkdownInline>> row, Font font)
        {
            for (int c = 0; c < columns; c++)
            {
                List<Atom> atoms = Flatten(row[c], new Style(font, null, null, false, false, -1), node: null, measuring: true);

                float sum = atoms.Sum(a => a.Width);
                float widest = atoms.Where(a => !a.IsSpace).Select(a => a.Width).DefaultIfEmpty(0f).Max();

                natural[c] = Math.Max(natural[c], sum + padX * 2f);
                minimum[c] = Math.Max(minimum[c], widest + padX * 2f);
            }
        }

        Consider(table.Header, headerFont);
        foreach (IReadOnlyList<IReadOnlyList<MarkdownInline>> row in table.Rows) Consider(row, _font);

        float totalNatural = natural.Sum();
        float totalMinimum = minimum.Sum();
        var widths = new float[columns];

        for (int c = 0; c < columns; c++)
        {
            widths[c] = totalNatural <= width ? natural[c]
                : totalMinimum >= width ? minimum[c]
                : minimum[c] + (width - totalMinimum) * (natural[c] - minimum[c]) / Math.Max(1f, totalNatural - totalMinimum);
        }

        float tableWidth = widths.Sum();
        bool rtl = _align == HorizontalContentAlignment.Right;
        float tableX = rtl ? x + width - tableWidth : x;
        float top = y;

        var tableNode = new MarkdownNode(AccessibilityRole.Table, string.Empty);

        // the backgrounds go under everything; their size is known once the row is laid out
        int rowIndex = 0;

        float LayoutRow(IReadOnlyList<IReadOnlyList<MarkdownInline>> row, float rowTop, bool header)
        {
            int background = -1;

            Color fill = header ? _viewer.TableHeaderBackground
                : rowIndex % 2 == 1 ? _viewer.TableStripeColor
                : default;

            if (fill.A > 0)
            {
                background = _layout.Boxes.Count;
                _layout.Boxes.Add(new MarkdownBox(MarkdownBoxKind.Fill, default, fill));
            }

            var rowNode = new MarkdownNode(AccessibilityRole.Row, string.Empty);
            float bottom = rowTop;
            float cellX = tableX;

            for (int c = 0; c < columns; c++)
            {
                // right to left, the first column is at the right
                int column = rtl ? columns - 1 - c : c;
                float cellWidth = widths[column];

                HorizontalContentAlignment align = table.Alignments[column] switch
                {
                    TableAlignment.Center => HorizontalContentAlignment.Center,
                    TableAlignment.Right => HorizontalContentAlignment.Right,
                    TableAlignment.Left => HorizontalContentAlignment.Left,
                    _ => _align,
                };

                var cellNode = new MarkdownNode(
                    header ? AccessibilityRole.ColumnHeader : AccessibilityRole.Cell,
                    MarkdownInline.PlainText(row[column]));

                float end = LayoutInlines(row[column], cellX + padX, rowTop + padY, Math.Max(1f, cellWidth - padX * 2f),
                    new Style(header ? headerFont : _font, context.TextColor, null, false, false, -1), align, cellNode);

                bottom = Math.Max(bottom, end + padY);

                cellNode.Bounds = new Rectangle(new Point(cellX, rowTop), new Size(cellWidth, 0f));
                rowNode.Children.Add(cellNode);

                cellX += cellWidth;
            }

            // an empty row is still a row
            bottom = Math.Max(bottom, rowTop + LineHeight(_font) + padY * 2f);

            if (background >= 0)
            {
                _layout.Boxes[background] = _layout.Boxes[background] with
                {
                    Bounds = new Rectangle(new Point(tableX, rowTop), new Size(tableWidth, bottom - rowTop)),
                };
            }

            foreach (MarkdownNode cell in rowNode.Children)
                cell.Bounds = new Rectangle(cell.Bounds.Position, new Size(cell.Bounds.Width, bottom - rowTop));

            // the cells were added in screen order; the reader goes in reading order
            if (rtl) rowNode.Children.Reverse();

            rowNode.Bounds = new Rectangle(new Point(tableX, rowTop), new Size(tableWidth, bottom - rowTop));
            tableNode.Children.Add(rowNode);

            return bottom;
        }

        var rowEdges = new List<float> { y };

        y = LayoutRow(table.Header, y, header: true);
        rowEdges.Add(y);

        foreach (IReadOnlyList<IReadOnlyList<MarkdownInline>> row in table.Rows)
        {
            rowIndex++;
            y = LayoutRow(row, y, header: false);
            rowEdges.Add(y);
        }

        // the grid: a line under every row, between the columns, and a frame
        Color border = _viewer.TableBorderColor;

        if (border.A > 0)
        {
            foreach (float edge in rowEdges)
                _layout.Boxes.Add(new MarkdownBox(MarkdownBoxKind.Fill, new Rectangle(new Point(tableX, edge - 0.5f), new Size(tableWidth, 1f)), border));

            float columnX = tableX;

            for (int c = 0; c <= columns; c++)
            {
                _layout.Boxes.Add(new MarkdownBox(MarkdownBoxKind.Fill, new Rectangle(new Point(columnX - 0.5f, top), new Size(1f, y - top)), border));

                if (c < columns) columnX += widths[rtl ? columns - 1 - c : c];
            }
        }

        tableNode.Bounds = new Rectangle(new Point(tableX, top), new Size(tableWidth, y - top));
        parent.Children.Add(tableNode);

        return y;
    }

    // ===== inlines =====

    private int AddLink(string url, string? title, string text)
    {
        _layout.Links.Add(new MarkdownLink(url, title, text));
        return _layout.Links.Count - 1;
    }

    /// <summary>Inlines turned into atoms: words, spaces and breaks, each in its look.</summary>
    /// <param name="measuring">Only the widths are wanted — a table sizing its
    /// columns: the links are not registered, they will be when laid out.</param>
    private List<Atom> Flatten(IReadOnlyList<MarkdownInline> inlines, Style style, MarkdownNode? node = null, bool measuring = false)
    {
        var atoms = new List<Atom>();
        Flatten(inlines, style, atoms, node, measuring);
        return atoms;
    }

    private void Flatten(IReadOnlyList<MarkdownInline> inlines, Style style, List<Atom> atoms, MarkdownNode? node, bool measuring)
    {
        foreach (MarkdownInline inline in inlines)
        {
            switch (inline)
            {
                case TextInline text:
                    AddWords(text.Text, style, atoms);
                    break;

                case CodeInline code:
                    {
                        Color? color = _viewer.CodeTextColor.A > 0 ? _viewer.CodeTextColor : style.Color;
                        Color? background = _viewer.CodeBackground.A > 0 ? _viewer.CodeBackground : null;

                        // a link's code stays the link's color
                        AddWords(code.Code, style with
                        {
                            Font = _codeFont.WithSize(style.Font.Size * 0.9f),
                            Color = style.Link >= 0 ? style.Color : color,
                            Background = background,
                        }, atoms);
                        break;
                    }

                case LineBreakInline:
                    atoms.Add(new Atom(string.Empty, style, 0f, IsSpace: false, IsBreak: true));
                    break;

                case EmphasisInline emphasis:
                    Flatten(emphasis.Children, style with { Font = style.Font.Italic() }, atoms, node, measuring);
                    break;

                case StrongInline strong:
                    Flatten(strong.Children, style with { Font = style.Font.Bold() }, atoms, node, measuring);
                    break;

                case StrikethroughInline strike:
                    Flatten(strike.Children, style with { Strike = true }, atoms, node, measuring);
                    break;

                case LinkInline link:
                    {
                        if (measuring)
                        {
                            Flatten(link.Children, style, atoms, node, measuring);
                            break;
                        }

                        int index = style.Link >= 0
                            ? style.Link
                            : AddLink(link.Url, link.Title, MarkdownInline.PlainText(link.Children));

                        if (style.Link < 0 && node is not null)
                            node.Children.Add(new MarkdownNode(AccessibilityRole.Link, _layout.Links[index].Text) { Link = index });

                        Flatten(link.Children, style with
                        {
                            Color = _viewer.LinkColor,
                            Underline = true,
                            Link = index,
                        }, atoms, node, measuring);
                        break;
                    }

                case ImageInline image:
                    // a picture inside a line of text is told by its alt text
                    AddWords(image.Alt.Length > 0 ? image.Alt : image.Url, style with { Font = style.Font.Italic() }, atoms);
                    break;
            }
        }
    }

    private static void AddWords(string text, Style style, List<Atom> atoms)
    {
        int i = 0;

        while (i < text.Length)
        {
            bool space = char.IsWhiteSpace(text[i]) && text[i] != '\u00A0';
            int start = i;

            while (i < text.Length && (char.IsWhiteSpace(text[i]) && text[i] != '\u00A0') == space) i++;

            string piece = text[start..i];

            atoms.Add(new Atom(piece, style, Measure(piece, style.Font), IsSpace: space, IsBreak: false));
        }
    }

    /// <summary>Wrap inlines into lines across the width, and add them to the layout.
    /// Returns the bottom of the last line.</summary>
    private float LayoutInlines(IReadOnlyList<MarkdownInline> inlines, float x, float y, float width, Style style, HorizontalContentAlignment align, MarkdownNode node)
    {
        List<Atom> atoms = Flatten(inlines, style, node);

        if (atoms.Count == 0) return y;

        var line = new List<Atom>();
        float lineWidth = 0f;

        void Commit(bool forced)
        {
            // spaces at the end of a line take no room
            while (line.Count > 0 && line[^1].IsSpace)
            {
                lineWidth -= line[^1].Width;
                line.RemoveAt(line.Count - 1);
            }

            if (line.Count == 0 && !forced) return;

            y = EmitLine(line, lineWidth, x, y, width, style.Font, align);

            line.Clear();
            lineWidth = 0f;
        }

        foreach (Atom atom in atoms)
        {
            if (atom.IsBreak)
            {
                Commit(forced: true);
                continue;
            }

            if (atom.IsSpace)
            {
                // no space at the start of a line
                if (line.Count == 0) continue;

                line.Add(atom);
                lineWidth += atom.Width;
                continue;
            }

            if (lineWidth + atom.Width > width && line.Any(a => !a.IsSpace))
                Commit(forced: false);

            if (atom.Width <= width)
            {
                line.Add(atom);
                lineWidth += atom.Width;
                continue;
            }

            // a word longer than the line: cut where it must be cut
            List<string> pieces = BreakToWidth(atom.Text, atom.Style.Font, width);

            for (int p = 0; p < pieces.Count; p++)
            {
                float pieceWidth = Measure(pieces[p], atom.Style.Font);

                line.Add(atom with { Text = pieces[p], Width = pieceWidth });
                lineWidth += pieceWidth;

                if (p < pieces.Count - 1) Commit(forced: false);
            }
        }

        Commit(forced: false);

        return y;
    }

    /// <summary>A wrapped line: its atoms merged into runs of one look, and the
    /// places of its links noted.</summary>
    private float EmitLine(List<Atom> atoms, float lineWidth, float x, float y, float width, Font baseFont, HorizontalContentAlignment align)
    {
        float height = LineHeight(baseFont);

        foreach (Atom atom in atoms)
            height = Math.Max(height, LineHeight(atom.Style.Font));

        var bounds = new Rectangle(new Point(x, y), new Size(width, height));

        if (atoms.Count > 0)
        {
            var runs = new List<TextRun>();
            var links = new List<int>();

            int i = 0;

            while (i < atoms.Count)
            {
                Style look = atoms[i].Style;
                int start = i;

                while (i < atoms.Count && atoms[i].Style == look) i++;

                string text = string.Concat(atoms.Skip(start).Take(i - start).Select(a => a.Text));

                runs.Add(new TextRun(text)
                {
                    Font = look.Font,
                    Color = look.Color,
                    Background = look.Background,
                    Underline = look.Underline,
                    Strikethrough = look.Strike,
                });

                links.Add(look.Link);
            }

            _layout.Lines.Add(new MarkdownLine(bounds, [.. runs], [.. links], align));

            // where the links are: the atoms' widths from the line's start
            float start0 = align switch
            {
                HorizontalContentAlignment.Right => x + width - lineWidth,
                HorizontalContentAlignment.Center => x + (width - lineWidth) / 2f,
                _ => x,
            };

            float cursor = start0;
            int current = -1;
            float linkStart = 0f;

            void Close(float end)
            {
                if (current < 0) return;

                _layout.Links[current].Rects.Add(new Rectangle(new Point(linkStart, y), new Size(end - linkStart, height)));
                current = -1;
            }

            foreach (Atom atom in atoms)
            {
                if (atom.Style.Link != current)
                {
                    Close(cursor);

                    if (atom.Style.Link >= 0)
                    {
                        current = atom.Style.Link;
                        linkStart = cursor;
                    }
                }

                cursor += atom.Width;
            }

            Close(cursor);
        }

        return y + height;
    }
}