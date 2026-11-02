﻿using ZeppelinForms.Animation;
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
/// <remarks>
/// A row takes the pseudo-classes of its node: <c>:expanded</c>, <c>:selected</c>,
/// <c>:checked</c> and <c>:indeterminate</c> — <c>TreeViewItem:expanded { GlyphColor: … }</c>
/// in a style sheet. The expander is <see cref="ExpanderGlyph"/>, or icons of one's own
/// (<see cref="CollapsedIcon"/>, <see cref="ExpandedIcon"/>), or anything a row of a
/// derived tree draws in <see cref="DrawExpander"/>.
/// </remarks>
public partial class TreeViewItem : DecoratedControl
{
    /// <summary>The width of the strip for the expander. It is also its click zone:
    /// a click in it expands the node, a click to the right selects the row.</summary>
    [Obsolete("The strip is styled since 0.20: ExpanderWidth, 18 unless a style sets it.")]
    public const float GlyphWidth = 18f;

    /// <summary>The strip of the check box, while the tree shows them. A click in it
    /// checks the node without selecting it.</summary>
    public const float CheckBoxWidth = 18f;

    /// <summary>The strip of the icon, while any node of the tree has one: the captions
    /// of nodes with and without an icon line up.</summary>
    public const float IconWidth = 20f;

    private const float CheckBoxSize = 13f;
    private const float IconSize = 16f;

    /// <summary>How long the expander turns. Short: it accompanies the rows appearing,
    /// which don't wait for it.</summary>
    private const int ExpandDurationMs = 120;

    private const string ExpandAnimation = "expander";

    private readonly TreeView _owner;

    public TreeNode Node { get; }

    /// <summary>The expander between collapsed, 0, and expanded, 1 — on its way while it turns.</summary>
    private float _expandProgress;

    /// <summary>The expansion the row shows or turns towards: a node expanded again
    /// in the middle of the turn turns back from where it is.</summary>
    private bool _shownExpanded;

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

        // a row made for a node already expanded shows it at once: it is not turning,
        // it comes into sight that way
        _shownExpanded = node.IsExpanded;
        _expandProgress = _shownExpanded ? 1f : 0f;

        SyncPseudoClasses();
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

    /// <summary>What the third state of the check box shows — a parent some of whose
    /// children are checked: as <see cref="CheckBox.IndeterminateGlyph"/> does.</summary>
    [Styled]
    public partial IndeterminateGlyph IndeterminateGlyph { get; set; }
    private static IndeterminateGlyph IndeterminateGlyphDefault => IndeterminateGlyph.Dash;

    // ===== the expander =====

    /// <summary>The shape of the expander of a node with children: the chevron, a filled
    /// triangle, the classic plus and minus in a box, or none — the node then expands
    /// with a double click and the keys only.</summary>
    /// <remarks><see cref="CollapsedIcon"/> takes the place of any of them.</remarks>
    [Styled(Category = "Expander")]
    public partial ExpanderGlyph ExpanderGlyph { get; set; }
    private static ExpanderGlyph ExpanderGlyphDefault => ExpanderGlyph.Chevron;

    /// <summary>The width of the strip for the expander: its click zone as well, see
    /// <see cref="GlyphWidth"/> — the constant it was until 0.20. Zero leaves no strip.</summary>
    [Styled(Category = "Expander", AffectsLayout = true)]
    public partial float ExpanderWidth { get; set; }
    private static float ExpanderWidthDefault => 18f;

    /// <summary>The size of the glyph across: the chevron's and the triangle's height,
    /// the side of the plus-minus box.</summary>
    [Styled(Category = "Expander")]
    public partial float ExpanderGlyphSize { get; set; }
    private static float ExpanderGlyphSizeDefault => 8f;

    /// <summary>An icon of one's own for the expander of a collapsed node, drawn pointing
    /// to the start — right, as the chevron does. Without <see cref="ExpandedIcon"/> it
    /// turns a quarter clockwise as the node expands; a path icon takes
    /// <see cref="GlyphColor"/>.</summary>
    [Styled(Category = "Expander")]
    public partial IconSource? CollapsedIcon { get; set; }

    /// <summary>The icon of an expanded node's expander, in place of the turned
    /// <see cref="CollapsedIcon"/>: a closed and an open folder, say.</summary>
    [Styled(Category = "Expander")]
    public partial IconSource? ExpandedIcon { get; set; }

    private string Text => _owner.GetItemText(Node);

    // minus one: TreeView has a service root, and the top-level nodes have
    // Level == 1 — without the correction the whole tree is shifted by an extra step
    private float IndentWidth => Math.Max(0, Node.Level - 1) * _owner.Indent;

    private float ExpanderStrip => Math.Max(0f, ExpanderWidth);

    private float CheckBoxStrip => _owner.CheckBoxes ? CheckBoxWidth : 0f;

    private float IconStrip => _owner.ShowsIcons ? IconWidth : 0f;

    /// <summary>Where the caption starts, from the content's left edge.</summary>
    private float CaptionLeft => IndentWidth + ExpanderStrip + CheckBoxStrip + IconStrip;

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

    /// <summary>The node's state changed — expanded, checked, selected: the
    /// pseudo-classes follow it, and the expander turns to it. Called by the tree.</summary>
    internal void SyncState()
    {
        SyncPseudoClasses();

        if (Node.IsExpanded == _shownExpanded) return;

        _shownExpanded = Node.IsExpanded;

        float target = _shownExpanded ? 1f : 0f;

        // the turn is decoration: with reduced motion, or a row not in a form, it is there at once
        if (Motion.IsReduced || FindOwner() is null)
        {
            this.StopAnimation(ExpandAnimation);
            _expandProgress = target;
            InvalidateVisual();
            return;
        }

        this.Animate(
            ExpandAnimation, _expandProgress, target, TimeSpan.FromMilliseconds(ExpandDurationMs),
            Interpolators.Float,
            value => { _expandProgress = value; InvalidateVisual(); },
            Easing.EaseOut);
    }

    private void SyncPseudoClasses()
    {
        SetPseudoClass(PseudoClass.Expanded, Node.IsExpanded && Node.HasChildren);
        SetPseudoClass(PseudoClass.Selected, _owner.IsSelected(Node));
        SetPseudoClass(PseudoClass.Checked, Node.CheckedState == CheckedState.Checked);
        SetPseudoClass(PseudoClass.Indeterminate, Node.CheckedState == CheckedState.Intermediate);
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
        float strip = ExpanderStrip;

        if (Node.HasChildren && strip > 0f)
        {
            DrawExpander(
                g,
                new Rectangle(new Point(glyphLeft, content.Y), new Size(strip, content.Height)),
                _expandProgress);
        }

        float x = glyphLeft + strip;

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

    /// <summary>Draw the expander of a node with children into its strip.</summary>
    /// <param name="g">The graphics of the row.</param>
    /// <param name="bounds">The expander's strip: <see cref="ExpanderWidth"/> wide, the
    /// row's content high, in the row's coordinates.</param>
    /// <param name="progress">0 — collapsed, 1 — expanded, in between while it turns.</param>
    /// <remarks>An override draws an expander of its own: a derived tree makes such rows
    /// in <see cref="TreeView.CreateItem"/>. The strip stays the click zone.</remarks>
    protected virtual void DrawExpander(Graphics g, Rectangle bounds, float progress)
    {
        var center = new Point(bounds.X + bounds.Width / 2f, bounds.Y + bounds.Height / 2f);

        if (CollapsedIcon is { } collapsed)
        {
            DrawExpanderIcon(g, bounds, center, collapsed, progress);
            return;
        }

        float radius = Math.Max(0f, ExpanderGlyphSize) / 2f;

        switch (ExpanderGlyph)
        {
            case ExpanderGlyph.Chevron:
                // the exact shapes at both ends: an expander at rest draws as it did
                // before it could turn, to the pixel
                if (progress <= 0f || progress >= 1f)
                {
                    Glyphs.DrawChevron(g, center, radius, progress >= 1f, GlyphColor);
                    break;
                }

                ReadOnlySpan<Point> chevron =
                [
                    Turn(center, -0.6f * radius, -radius, progress),
                    Turn(center, 0.8f * radius, 0f, progress),
                    Turn(center, -0.6f * radius, radius, progress),
                ];

                g.DrawPolyline(chevron, GlyphColor, 1.8f);
                break;

            case ExpanderGlyph.Triangle:
                ReadOnlySpan<Point> triangle =
                [
                    Turn(center, -0.5f * radius, -radius, progress),
                    Turn(center, 0.75f * radius, 0f, progress),
                    Turn(center, -0.5f * radius, radius, progress),
                ];

                g.FillPolygon(triangle, GlyphColor);
                break;

            case ExpanderGlyph.PlusMinus:
                DrawPlusMinus(g, center, radius, progress);
                break;
        }
    }

    /// <summary>A point of the collapsed glyph, given from the center, turned a quarter
    /// clockwise times the progress — the collapsed shape points right, the expanded down.</summary>
    private static Point Turn(Point center, float dx, float dy, float progress)
    {
        float angle = progress * MathF.PI / 2f;
        float cos = MathF.Cos(angle);
        float sin = MathF.Sin(angle);

        return new Point(center.X + dx * cos - dy * sin, center.Y + dx * sin + dy * cos);
    }

    /// <summary>The classic box with a plus, and a minus once expanded. It doesn't turn:
    /// the vertical bar fades out instead.</summary>
    private void DrawPlusMinus(Graphics g, Point center, float radius, float progress)
    {
        // whole pixels and a half: a one-pixel box stroked on a pixel's middle is crisp
        float half = MathF.Round(radius);
        float cx = MathF.Round(center.X) + 0.5f;
        float cy = MathF.Round(center.Y) + 0.5f;
        float bar = half - 2f;

        g.DrawRectangle(
            new Rectangle(new Point(cx - half, cy - half), new Size(half * 2f, half * 2f)),
            GlyphColor, 1f);

        if (bar <= 0f) return;

        g.DrawLine(new Point(cx - bar, cy), new Point(cx + bar, cy), GlyphColor, 1f);

        if (progress >= 1f) return;

        Color vertical = GlyphColor.WithA((byte)(GlyphColor.A * (1f - progress)));

        g.DrawLine(new Point(cx, cy - bar), new Point(cx, cy + bar), vertical, 1f);
    }

    private void DrawExpanderIcon(Graphics g, Rectangle bounds, Point center, IconSource collapsed, float progress)
    {
        // as big as the strip and the row allow, with a little air around it
        float side = Math.Max(0f, Math.Min(bounds.Width, bounds.Height) - 4f);
        var square = new Rectangle(new Point(center.X - side / 2f, center.Y - side / 2f), new Size(side, side));

        if (ExpandedIcon is { } expanded)
        {
            (Node.IsExpanded ? expanded : collapsed).Draw(g, square, GlyphColor);
            return;
        }

        if (progress <= 0f)
        {
            collapsed.Draw(g, square, GlyphColor);
            return;
        }

        g.Save();
        g.Translate(center.X, center.Y);
        g.Rotate(progress * 90f);
        g.Translate(-center.X, -center.Y);

        collapsed.Draw(g, square, GlyphColor);

        g.Restore();
    }

    private void DrawCheckBox(Graphics g, Rectangle box)
    {
        var radius = new CornerRadius(2f);

        switch (Node.CheckedState)
        {
            case CheckedState.Checked:
                break;

            case CheckedState.Intermediate:
                DrawIndeterminate(g, box, radius);
                return;

            default:
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

    /// <summary>The third state, as the check box draws it: a dash or a dot on the filled
    /// box, or the classic small square in an empty one.</summary>
    private void DrawIndeterminate(Graphics g, Rectangle box, CornerRadius radius)
    {
        if (IndeterminateGlyph == IndeterminateGlyph.Square)
        {
            g.DrawRoundRectangle(box, radius, GlyphColor, 1f);

            var inner = new Rectangle(
                new Point(box.X + box.Width * 0.25f, box.Y + box.Height * 0.25f),
                new Size(box.Width * 0.5f, box.Height * 0.5f));

            g.FillRoundRectangle(inner, new CornerRadius(box.Width * 0.08f), CheckColor);
            return;
        }

        g.FillRoundRectangle(box, radius, CheckColor);

        if (IndeterminateGlyph == IndeterminateGlyph.Dot)
        {
            float diameter = box.Width * 0.36f;

            g.FillEllipse(
                new Rectangle(
                    new Point(box.X + (box.Width - diameter) / 2f, box.Y + (box.Height - diameter) / 2f),
                    new Size(diameter, diameter)),
                CheckGlyphColor);
            return;
        }

        g.DrawLine(
            new Point(box.X + box.Width * 0.24f, box.Y + box.Height * 0.5f),
            new Point(box.X + box.Width * 0.76f, box.Y + box.Height * 0.5f),
            CheckGlyphColor,
            box.Width * 0.14f);
    }

    /// <summary>What part of the row a point of the form falls on.</summary>
    private RowPart PartAt(Point location)
    {
        Point abs = GetAbsolutePosition();
        float localX = location.X - abs.X - Padding.Left;
        float strip = ExpanderStrip;

        // a hit in the expander strip toggles rather than selects: otherwise
        // a node with children couldn't be clicked without collapsing it
        if (Node.HasChildren && localX >= IndentWidth && localX < IndentWidth + strip)
            return RowPart.Expander;

        float checkLeft = IndentWidth + strip;

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
                // the third state checks, as it does in every tree with one
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

/// <summary>The shape of a tree node's expander, see <see cref="TreeViewItem.ExpanderGlyph"/>.</summary>
public enum ExpanderGlyph : byte
{
    /// <summary>A chevron, right when collapsed, down when expanded: Fluent, macOS.</summary>
    Chevron,

    /// <summary>A filled triangle, turning the same way: GTK, older macOS.</summary>
    Triangle,

    /// <summary>A plus in a box, a minus once expanded: classic Windows.</summary>
    PlusMinus,

    /// <summary>No expander: the node expands with a double click and the keys only.</summary>
    None,
}
