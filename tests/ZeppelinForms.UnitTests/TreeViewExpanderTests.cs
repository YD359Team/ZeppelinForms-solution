using Xunit;
using ZeppelinForms.Animation;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Tree;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>0.20: the rows of a tree as styles see them — their nodes' pseudo-classes, the
/// expander's shape, size and icons, and its turn.</summary>
[Collection("Platform")]
public class TreeViewExpanderTests
{
    /// <summary>Records the shapes an expander is drawn with.</summary>
    private sealed class RecordingGraphics : HeadlessGraphics
    {
        public List<Point[]> Polylines { get; } = [];
        public List<Point[]> Polygons { get; } = [];
        public List<Rectangle> Rectangles { get; } = [];
        public List<float> Rotations { get; } = [];
        public List<Rectangle> Paths { get; } = [];

        public override void DrawPolyline(ReadOnlySpan<Point> points, Color color, float width) =>
            Polylines.Add(points.ToArray());

        public override void FillPolygon(ReadOnlySpan<Point> points, Color color) => Polygons.Add(points.ToArray());

        public override void DrawRectangle(Rectangle rect, Color color, float width) => Rectangles.Add(rect);

        public override void Rotate(float degrees) => Rotations.Add(degrees);

        public override void DrawSvgPath(string pathData, Rectangle rect, Color color, float strokeWidth = 0f) =>
            Paths.Add(rect);
    }

    /// <summary>"Parent" with "Child", collapsed; no check boxes, no icons.</summary>
    private static (Form Form, TreeView Tree, TreeNode Parent, TreeNode Child) Create()
    {
        var tree = new TreeView
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        var child = new TreeNode("Child");
        var parent = new TreeNode("Parent") { Children = { child } };

        tree.Nodes.Add(parent);

        var form = new Form { Size = new Size(400, 300), Content = tree };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return (form, tree, parent, child);
    }

    private static TreeViewItem Row(TreeView tree, TreeNode node) =>
        tree.Children.OfType<VirtualizingStackPanel>().Single()
            .Children.OfType<TreeViewItem>().Single(row => ReferenceEquals(row.Node, node));

    private static RecordingGraphics Record(TreeViewItem row)
    {
        var g = new RecordingGraphics();
        row.Draw(g);
        return g;
    }

    private static void WithMotion(MotionPreference preference, Action body)
    {
        MotionPreference before = Motion.Preference;

        try
        {
            Motion.Preference = preference;
            body();
        }
        finally
        {
            Motion.Preference = before;
        }
    }

    // ===== pseudo-classes =====

    [Fact]
    public void ARowTakesItsNodesPseudoClasses()
    {
        var (form, tree, parent, child) = Create();
        tree.CheckBoxes = true;
        form.UpdateLayout();

        TreeViewItem row = Row(tree, parent);
        Assert.False(row.HasPseudoClass(PseudoClass.Expanded));

        parent.Expand();
        form.UpdateLayout();

        // the same row: rows are not made again on an expand
        Assert.Same(row, Row(tree, parent));
        Assert.True(row.HasPseudoClass(PseudoClass.Expanded));

        tree.SelectedNode = parent;
        Assert.True(row.HasPseudoClass(PseudoClass.Selected));

        child.IsChecked = true;
        Assert.True(row.HasPseudoClass(PseudoClass.Checked));
        Assert.True(Row(tree, child).HasPseudoClass(PseudoClass.Checked));

        parent.Children.Add(new TreeNode("Second"));
        Assert.True(row.HasPseudoClass(PseudoClass.Indeterminate));
        Assert.False(row.HasPseudoClass(PseudoClass.Checked));
    }

    [Fact]
    public void AStyleSheetStylesTheExpandedRows()
    {
        var (form, tree, parent, _) = Create();

        StyleSheet sheet = StyleSheet.Parse(
            "TreeViewItem:expanded { ExpanderGlyph: PlusMinus; GlyphColor: #FF0000; }", "tree.zss");
        Assert.Empty(sheet.Diagnostics);
        form.Styles.Add(sheet);

        TreeViewItem row = Row(tree, parent);
        Assert.Equal(ExpanderGlyph.Chevron, row.ExpanderGlyph);

        parent.Expand();

        Assert.Equal(ExpanderGlyph.PlusMinus, row.ExpanderGlyph);
        Assert.Equal(new Color(255, 255, 0, 0), row.GlyphColor);
    }

    // ===== shape and size =====

    [Fact]
    public void EachGlyphDrawsItsShape()
    {
        var (_, tree, parent, _) = Create();
        TreeViewItem row = Row(tree, parent);

        Assert.Single(Record(row).Polylines);

        row.ExpanderGlyph = ExpanderGlyph.Triangle;
        Assert.Single(Record(row).Polygons);

        row.ExpanderGlyph = ExpanderGlyph.PlusMinus;
        RecordingGraphics plusMinus = Record(row);
        Assert.Single(plusMinus.Rectangles);
        Assert.Empty(plusMinus.Polylines);

        row.ExpanderGlyph = ExpanderGlyph.None;
        RecordingGraphics none = Record(row);
        Assert.Empty(none.Polylines);
        Assert.Empty(none.Polygons);
        Assert.Empty(none.Rectangles);
    }

    [Fact]
    public void AnExpanderAtRestDrawsAsBefore()
    {
        var (_, tree, parent, _) = Create();
        TreeViewItem row = Row(tree, parent);

        Point[] collapsed = Record(row).Polylines.Single();

        // the collapsed chevron points right: its tip is the rightmost point, in the middle
        Assert.True(collapsed[1].X > collapsed[0].X);
        Assert.Equal(collapsed[0].X, collapsed[2].X);
    }

    [Fact]
    public void ExpanderWidthMovesTheCaptionAndIsTheClickZone()
    {
        var (form, tree, parent, _) = Create();
        TreeViewItem row = Row(tree, parent);

        float before = row.CaptionBounds.X;

        row.ExpanderWidth = 30f;
        form.UpdateLayout();

        Assert.Equal(before + 12f, row.CaptionBounds.X);

        // a click 25 px in is on the expander now: the node expands
        Point at = row.GetAbsolutePosition();
        HeadlessInput.Click(form, at.X + row.Padding.Left + 25f, at.Y + row.ActualSize.Height / 2f);

        Assert.True(parent.IsExpanded);
        Assert.Null(tree.SelectedNode);

        // no strip: the same click is on the caption and selects
        row.ExpanderWidth = 0f;
        form.UpdateLayout();
        HeadlessInput.Click(form, at.X + row.Padding.Left + 2f, at.Y + row.ActualSize.Height / 2f);

        Assert.True(parent.IsExpanded);
        Assert.Same(parent, tree.SelectedNode);
    }

    // ===== icons =====

    [Fact]
    public void ACollapsedIconTurnsWithoutAnExpandedOne()
    {
        WithMotion(MotionPreference.Reduced, () =>
        {
            var (_, tree, parent, _) = Create();
            TreeViewItem row = Row(tree, parent);

            row.CollapsedIcon = IconSource.FromPath("M0 0L10 5L0 10z");

            RecordingGraphics collapsed = Record(row);
            Assert.Single(collapsed.Paths);
            Assert.Empty(collapsed.Rotations);

            parent.Expand();
            Assert.Equal([90f], Record(row).Rotations);

            row.ExpandedIcon = IconSource.FromPath("M0 0L10 0L5 10z");
            RecordingGraphics expanded = Record(row);
            Assert.Single(expanded.Paths);
            Assert.Empty(expanded.Rotations);
        });
    }

    // ===== the turn =====

    [Fact]
    public void TheExpanderTurnsOverTime()
    {
        WithMotion(MotionPreference.Full, () =>
        {
            var (form, tree, parent, _) = Create();
            TreeViewItem row = Row(tree, parent);

            parent.Expand();

            // not turned yet: no frame has gone by
            Point[] start = Record(row).Polylines.Single();
            Assert.True(start[1].X > start[0].X);

            form.Clock.Advance(TimeSpan.FromMilliseconds(60));
            Point[] halfway = Record(row).Polylines.Single();
            Assert.NotEqual(start[1], halfway[1]);

            form.Clock.Advance(TimeSpan.FromMilliseconds(200));
            Point[] end = Record(row).Polylines.Single();

            // expanded, the chevron points down: its tip is the lowest point
            Assert.True(end[1].Y > end[0].Y);
            Assert.Equal(end[0].Y, end[2].Y);
        });
    }

    [Fact]
    public void WithReducedMotionTheExpanderTurnsAtOnce()
    {
        WithMotion(MotionPreference.Reduced, () =>
        {
            var (_, tree, parent, _) = Create();
            TreeViewItem row = Row(tree, parent);

            parent.Expand();

            Point[] end = Record(row).Polylines.Single();
            Assert.True(end[1].Y > end[0].Y);
        });
    }
}
