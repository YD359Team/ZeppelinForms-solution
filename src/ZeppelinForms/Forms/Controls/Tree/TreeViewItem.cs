using ZeppelinForms.Core.Text;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Tools;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls.Tree;

/// <summary>Одна строка дерева: отступ по глубине, раскрыватель, текст.</summary>
public partial class TreeViewItem : DecoratedControl
{
    /// <summary>Ширина полосы под раскрыватель. Она же — зона его нажатия:
    /// клик в неё раскрывает узел, клик правее выделяет строку.</summary>
    public const float GlyphWidth = 18f;

    private readonly TreeView _owner;

    public TreeNode Node { get; }

    public TreeViewItem(TreeView owner, TreeNode node)
    {
        _owner = owner;
        Node = node;

        // ZF0006: styled-свойства в конструкторе только через SetControlDefault
        SetControlDefault(SelectionColorProperty, new Color(255, 205, 226, 252));
        SetControlDefault(GlyphColorProperty, new Color(255, 90, 90, 90));
        SetControlDefault(TextColorProperty, Colors.Black);
    }

    [Styled]
    public partial Color SelectionColor { get; set; }
    private static Color SelectionColorDefault => new(255, 205, 226, 252);

    [Styled]
    public partial Color GlyphColor { get; set; }
    private static Color GlyphColorDefault => new(255, 90, 90, 90);

    [Styled]
    public partial Color TextColor { get; set; }
    private static Color TextColorDefault => Colors.Black;

    private string Text => _owner.GetItemText(Node);

    private float IndentWidth => Node.Level * _owner.Indent;

    // фон, рамка и скругление рисует база — здесь выделение, глиф и текст
    protected override void DrawContent(Graphics g)
    {
        Rectangle content = ContentBounds;

        // выделение рисуем сами, а не через Background: Background —
        // обычное styled-свойство, и присвоение в него навсегда закрыло бы
        // строку от темы
        if (_owner.SelectedNode == Node)
            g.FillRectangle(content, SelectionColor);

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
        Point abs = GetAbsolutePosition();
        float localX = e.Location.X - abs.X - Padding.Left;

        // попадание в полосу раскрывателя — это переключение, а не выделение:
        // иначе по узлу с потомками нельзя было бы кликнуть, не свернув его
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
        if (!Node.HasChildren) return;

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