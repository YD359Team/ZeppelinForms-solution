using ZeppelinForms.Core.Text;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Tools;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls.Tree;

/// <summary>One tree row: indent by depth, expander, check box, icon, text.</summary>
public partial class TreeViewItem : DecoratedControl
{
    /// <summary>The width of the strip for the expander. It is also its click zone:
    /// a click in it expands the node, a click to the right selects the row.</summary>
    public const float GlyphWidth = 18f;

    /// <summary>The strip of the check box, while the tree shows them. A click in it
    /// checks the node without selecting it.</summary>
    public const float CheckBoxWidth = 18f;

    /// <summary>The strip of the icon, while any node of the tree has one: the captions
    /// of nodes with and without an icon line up.</summary>
    public const float IconWidth = 20f;

    private const float CheckBoxSize = 13f;
    private const float IconSize = 16f;

    private readonly TreeView _owner;

    public TreeNode Node { get; }

    public TreeViewItem(TreeView owner, TreeNode node)
    {
        _owner = owner;
        Node = node;

        // ZF0006: styled properties in a constructor only through SetControlDefault.
        // The alignment is overridden deliberately: UnitControl gives its descendants
        // Center, while a list row must take the whole width — otherwise the depth
        // indent is eaten by centering, and the nesting reads the wrong way round
        SetControlDefault(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        SetControlDefault(SelectionColorProperty, new Color(255, 205, 226, 252));
        SetControlDefault(GlyphColorProperty, new Color(255, 90, 90, 90));
    }

    [Styled]
    public partial Color SelectionColor { get; set; }
    private static Color SelectionColorDefault => new(255, 205, 226, 252);

    /// <summary>The row under the cursor. A row is clickable — it selects the node —
    /// so it shows that before the click.</summary>
    [Styled]
    public partial Color HoverColor { get; set; }
    private static Color HoverColorDefault => new(20, 0, 0, 0);

    [Styled]
    public partial Color GlyphColor { get; set; }
    private static Color GlyphColorDefault => new(255, 90, 90, 90);

    /// <summary>The fill of a checked check box.</summary>
    [Styled]
    public partial Color CheckColor { get; set; }
    private static Color CheckColorDefault => new(255, 0x0D, 0x6E, 0xFD);

    /// <summary>The check mark on <see cref="CheckColor"/>.</summary>
    [Styled]
    public partial Color CheckGlyphColor { get; set; }
    private static Color CheckGlyphColorDefault => Colors.White;

    private string Text => _owner.GetItemText(Node);

    // minus one: TreeView has a service root, and the top-level nodes have
    // Level == 1 — without the correction the whole tree is shifted by an extra step
    private float IndentWidth => Math.Max(0, Node.Level - 1) * _owner.Indent;

    private float CheckBoxStrip => _owner.CheckBoxes ? CheckBoxWidth : 0f;

    private float IconStrip => _owner.ShowsIcons ? IconWidth : 0f;

    /// <summary>Where the caption starts, from the content's left edge.</summary>
    private float CaptionLeft => IndentWidth + GlyphWidth + CheckBoxStrip + IconStrip;

    /// <summary>The caption's place in the row's own coordinates — where the editor
    /// stands while the node is renamed.</summary>
    internal Rectangle CaptionBounds
    {
        get
        {
            Rectangle content = ContentBounds;

            return new Rectangle(
                new Point(content.X + CaptionLeft, content.Y),
                new Size(Math.Max(0, content.Width - CaptionLeft), content.Height));
        }
    }

    // the background, border and corner radius are drawn by the base — here the selection, the glyph and the text
    protected override void DrawContent(Graphics g)
    {
        Rectangle content = ContentBounds;

        // the selection is drawn by us rather than through Background: Background
        // is an ordinary styled property, and assigning it would close the row
        // to the theme forever. The hover goes the same way and yields to the
        // selection: a selected row keeps its color when hovered
        if (_owner.IsSelected(Node))
            g.FillRectangle(content, SelectionColor);
        else if (IsHovered && IsEffectivelyEnabled)
            g.FillRectangle(content, HoverColor);

        float glyphLeft = content.X + IndentWidth;
        float middle = content.Y + content.Height / 2f;

        if (Node.HasChildren)
        {
            Glyphs.DrawChevron(
                g,
                new Point(glyphLeft + GlyphWidth / 2f, middle),
                4f,
                Node.IsExpanded,
                GlyphColor);
        }

        float x = glyphLeft + GlyphWidth;

        if (_owner.CheckBoxes)
        {
            DrawCheckBox(g, new Rectangle(
                new Point(x + (CheckBoxWidth - CheckBoxSize) / 2f, middle - CheckBoxSize / 2f),
                new Size(CheckBoxSize, CheckBoxSize)));

            x += CheckBoxWidth;
        }

        if (_owner.ShowsIcons)
        {
            _owner.GetItemIcon(Node)?.Draw(g, new Rectangle(
                new Point(x + (IconWidth - IconSize) / 2f, middle - IconSize / 2f),
                new Size(IconSize, IconSize)), TextColor);

            x += IconWidth;
        }

        // the caption is under the editor while the node is renamed
        if (string.IsNullOrEmpty(Text) || ReferenceEquals(_owner.EditingNode, Node)) return;

        var textRect = new Rectangle(
            new Point(x, content.Y),
            new Size(Math.Max(0, content.X + content.Width - x), content.Height));

        g.DrawText(Text, textRect, TextColor, EffectiveFont,
            HorizontalContentAlignment.Left, VerticalContentAlignment.Center);
    }

    private void DrawCheckBox(Graphics g, Rectangle box)
    {
        var radius = new CornerRadius(2f);

        if (!Node.IsChecked)
        {
            g.DrawRoundRectangle(box, radius, GlyphColor, 1f);
            return;
        }

        g.FillRoundRectangle(box, radius, CheckColor);

        // the same proportions as the check box's mark
        ReadOnlySpan<Point> mark =
        [
            new(box.X + box.Width * 0.22f, box.Y + box.Height * 0.52f),
            new(box.X + box.Width * 0.42f, box.Y + box.Height * 0.72f),
            new(box.X + box.Width * 0.78f, box.Y + box.Height * 0.30f),
        ];

        g.DrawPolyline(mark, CheckGlyphColor, box.Width * 0.14f);
    }

    /// <summary>What part of the row a point of the form falls on.</summary>
    private RowPart PartAt(Point location)
    {
        Point abs = GetAbsolutePosition();
        float localX = location.X - abs.X - Padding.Left;

        // a hit in the expander strip toggles rather than selects: otherwise
        // a node with children couldn't be clicked without collapsing it
        if (Node.HasChildren && localX >= IndentWidth && localX < IndentWidth + GlyphWidth)
            return RowPart.Expander;

        float checkLeft = IndentWidth + GlyphWidth;

        if (_owner.CheckBoxes && localX >= checkLeft && localX < checkLeft + CheckBoxWidth)
            return RowPart.CheckBox;

        return RowPart.Caption;
    }

    private enum RowPart { Caption, Expander, CheckBox }

    /// <summary>Selection on press, as in Explorer and as the list does: the right
    /// button too, so the context menu acts on the node under the cursor.</summary>
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        // the hit lands on the row, which is itself enabled, so the form's check
        // for a disabled hit doesn't stop a press inside a disabled tree
        if (!IsEffectivelyEnabled) return;

        switch (e.Button)
        {
            case MouseButton.Left when PartAt(e.Location) == RowPart.Caption:
                _owner.SelectFromPointer(Node, e.Modifiers);
                break;

            case MouseButton.Right:
                _owner.SelectForContextMenu(Node);
                break;
        }
    }

    protected override void OnClick(MouseClickEventArgs e)
    {
        if (!IsEffectivelyEnabled) return;

        switch (PartAt(e.Location))
        {
            case RowPart.Expander:
                Node.IsExpanded = !Node.IsExpanded;
                break;

            case RowPart.CheckBox:
                Node.IsChecked = !Node.IsChecked;
                break;
        }

        e.Handled = true;
    }

    /// <summary>A double click on the caption invokes the node; on the expander or the
    /// check box it is two clicks, each doing what one does.</summary>
    protected override void OnDoubleClick(MouseClickEventArgs e)
    {
        if (!IsEffectivelyEnabled || PartAt(e.Location) != RowPart.Caption) return;

        _owner.Invoke(Node);
        e.Handled = true;
    }

    /// <summary>The tree's menu for this node, see <see cref="TreeView.NodeContextMenu"/>.</summary>
    protected override List<MenuItem>? GetDefaultContextMenu() => _owner.NodeContextMenu?.Invoke(Node);

    protected override Size MeasureOverride(Size availableSize)
    {
        float textWidth = string.IsNullOrEmpty(Text)
            ? 0
            : TextMeasurer.Current.MeasureText(Text, EffectiveFont).Width;

        var content = new Size(
            CaptionLeft + textWidth + Padding.Horizontal,
            TextMeasurer.LineHeight(EffectiveFont) + Padding.Vertical);

        return ResolveSize(content, availableSize);
    }
}