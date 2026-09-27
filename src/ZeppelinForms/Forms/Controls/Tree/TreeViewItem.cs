using ZeppelinForms.Core.Text;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Tools;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls.Tree;

/// <summary>One tree row: indent by depth, expander, text.</summary>
public partial class TreeViewItem : DecoratedControl
{
    /// <summary>The width of the strip for the expander. It is also its click zone:
    /// a click in it expands the node, a click to the right selects the row.</summary>
    public const float GlyphWidth = 18f;

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

    private string Text => _owner.GetItemText(Node);

    // minus one: TreeView has a service root, and the top-level nodes have
    // Level == 1 — without the correction the whole tree is shifted by an extra step
    private float IndentWidth => Math.Max(0, Node.Level - 1) * _owner.Indent;

    // the background, border and corner radius are drawn by the base — here the selection, the glyph and the text
    protected override void DrawContent(Graphics g)
    {
        Rectangle content = ContentBounds;

        // the selection is drawn by us rather than through Background: Background
        // is an ordinary styled property, and assigning it would close the row
        // to the theme forever. The hover goes the same way and yields to the
        // selection: a selected row keeps its color when hovered
        if (_owner.SelectedNode == Node)
            g.FillRectangle(content, SelectionColor);
        else if (IsHovered && IsEffectivelyEnabled)
            g.FillRectangle(content, HoverColor);

        float glyphLeft = content.X + IndentWidth;

        if (Node.HasChildren)
        {
            Glyphs.DrawChevron(
                g,
                new Point(glyphLeft + GlyphWidth / 2f, content.Y + content.Height / 2f),
                4f,
                Node.IsExpanded,
                GlyphColor);
        }

        if (string.IsNullOrEmpty(Text)) return;

        var textRect = new Rectangle(
            new Point(glyphLeft + GlyphWidth, content.Y),
            new Size(Math.Max(0, content.Width - IndentWidth - GlyphWidth), content.Height));

        g.DrawText(Text, textRect, TextColor, EffectiveFont,
            HorizontalContentAlignment.Left, VerticalContentAlignment.Center);
    }

    protected override void OnClick(MouseClickEventArgs e)
    {
        // the hit lands on the row, which is itself enabled, so the form's check
        // for a disabled hit doesn't stop a click inside a disabled tree
        if (!IsEffectivelyEnabled) return;

        Point abs = GetAbsolutePosition();
        float localX = e.Location.X - abs.X - Padding.Left;

        // a hit in the expander strip toggles rather than selects: otherwise
        // a node with children couldn't be clicked without collapsing it
        if (Node.HasChildren && localX >= IndentWidth && localX < IndentWidth + GlyphWidth)
        {
            Node.IsExpanded = !Node.IsExpanded;
            e.Handled = true;
            return;
        }

        _owner.SelectedNode = Node;
        e.Handled = true;
    }

    protected override void OnDoubleClick(MouseClickEventArgs e)
    {
        if (!Node.HasChildren || !IsEffectivelyEnabled) return;

        Node.IsExpanded = !Node.IsExpanded;
        e.Handled = true;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        float textWidth = string.IsNullOrEmpty(Text)
            ? 0
            : TextMeasurer.Current.MeasureText(Text, EffectiveFont).Width;

        var content = new Size(
            IndentWidth + GlyphWidth + textWidth + Padding.Horizontal,
            TextMeasurer.Current.MeasureText("Wg", EffectiveFont).Height + Padding.Vertical);

        return ResolveSize(content, availableSize);
    }
}