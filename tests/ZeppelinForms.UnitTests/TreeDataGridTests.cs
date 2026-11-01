using System.Collections.ObjectModel;
using Xunit;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.DataGrid;
using ZeppelinForms.Forms.Controls.Tree;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.UnitTests;

/// <summary>0.20: a tree with columns — the rows of a grid are the expanded nodes, the
/// tree's column indents its cells and holds the expander, a sort orders siblings.</summary>
[Collection("Platform")]
public class TreeDataGridTests
{
    private sealed class Entry(string name, long size)
    {
        public string Name { get; set; } = name;

        public long Size { get; } = size;

        public ObservableCollection<Entry> Children { get; } = [];
    }

    /// <summary>"src" (30) with "b.cs" (20) and "a.cs" (10); "readme" (5).</summary>
    private static ObservableCollection<Entry> Entries(out Entry src, out Entry b, out Entry a, out Entry readme)
    {
        b = new Entry("b.cs", 20);
        a = new Entry("a.cs", 10);
        src = new Entry("src", 30) { Children = { b, a } };
        readme = new Entry("readme", 5);

        return [src, readme];
    }

    private static (Form Form, TreeDataGrid Grid) Show(ObservableCollection<Entry> entries)
    {
        var grid = new TreeDataGrid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Columns =
            {
                new DataGridViewColumn<Entry, string>(e => e.Name) { Width = GridLength.Fixed(160) },
                new DataGridViewColumn<Entry, long>(e => e.Size) { Width = GridLength.Fixed(80) },
            },
            ItemsSource = TreeItemsSource.Create(entries, e => e.Children),
        };

        var form = new Form { Size = new Size(400, 300), Content = grid };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return (form, grid);
    }

    private static List<string> Rows(TreeDataGrid grid) =>
        [.. Enumerable.Range(0, grid.Items.Count).Select(row => ((Entry)grid.RowItem(row)).Name)];

    /// <summary>A point in a cell of a row, <paramref name="x"/> from the column's left edge.</summary>
    private static Point At(TreeDataGrid grid, int row, float x)
    {
        Point origin = grid.GetAbsolutePosition();

        return new Point(origin.X + grid.Padding.Left + x,
            origin.Y + grid.Padding.Top + grid.HeaderHeight + row * grid.RowHeight + grid.RowHeight / 2f);
    }

    [Fact]
    public void TheRowsAreTheExpandedNodes()
    {
        var (_, grid) = Show(Entries(out Entry src, out _, out _, out _));

        Assert.Equal(["src", "readme"], Rows(grid));

        grid.NodeAt(0)!.Expand();
        Assert.Equal(["src", "b.cs", "a.cs", "readme"], Rows(grid));

        // the typed columns read the items themselves
        Assert.Same(src, grid.RowItem(0));

        grid.NodeAt(0)!.Collapse();
        Assert.Equal(["src", "readme"], Rows(grid));
    }

    [Fact]
    public void ASortOrdersSiblingsNotRows()
    {
        var (_, grid) = Show(Entries(out _, out _, out _, out _));
        grid.ExpandAll();

        grid.SortBy(1);

        // by size: readme (5) before src (30) — and src's children under it, a.cs first
        Assert.Equal(["readme", "src", "a.cs", "b.cs"], Rows(grid));

        grid.SortBy(1, descending: true);
        Assert.Equal(["src", "b.cs", "a.cs", "readme"], Rows(grid));

        grid.ClearSort();
        Assert.Equal(["src", "b.cs", "a.cs", "readme"], Rows(grid));
    }

    [Fact]
    public void TheExpanderTakesTheClickAndTheRestSelects()
    {
        var (form, grid) = Show(Entries(out Entry src, out _, out _, out _));

        // the padding, then the expander of a top-level node
        Point expander = At(grid, 0, grid.CellPadding.Left + 9f);
        HeadlessInput.Click(form, expander.X, expander.Y);

        Assert.True(grid.NodeAt(0)!.IsExpanded);
        Assert.Equal(-1, grid.SelectedIndex);

        Point caption = At(grid, 0, grid.CellPadding.Left + 60f);
        HeadlessInput.Click(form, caption.X, caption.Y);

        Assert.Same(src, grid.SelectedItem);
        Assert.True(grid.NodeAt(0)!.IsExpanded);
    }

    [Fact]
    public void TheSelectionStaysWithItsRecord()
    {
        var (_, grid) = Show(Entries(out _, out _, out _, out Entry readme));

        grid.SelectedIndex = 1;
        Assert.Same(readme, grid.SelectedItem);

        // rows come above it: it is still the one selected
        grid.NodeAt(0)!.Expand();

        Assert.Equal(3, grid.SelectedIndex);
        Assert.Same(readme, grid.SelectedItem);
    }

    [Fact]
    public void RightAndLeftOpenAndCloseTheTree()
    {
        var (form, grid) = Show(Entries(out Entry src, out Entry b, out _, out _));
        grid.Focus();
        grid.SelectedIndex = 0;

        HeadlessInput.PressKey(form, Key.Right);
        Assert.True(grid.NodeAt(0)!.IsExpanded);

        HeadlessInput.PressKey(form, Key.Right);
        Assert.Same(b, grid.SelectedItem);

        HeadlessInput.PressKey(form, Key.Left);
        Assert.Same(src, grid.SelectedItem);

        HeadlessInput.PressKey(form, Key.Left);
        Assert.False(grid.NodeAt(0)!.IsExpanded);
    }

    [Fact]
    public void TheDataFollowsTheSource()
    {
        ObservableCollection<Entry> entries = Entries(out Entry src, out _, out _, out _);
        var (_, grid) = Show(entries);
        grid.NodeAt(0)!.Expand();

        src.Children.Add(new Entry("c.cs", 1));
        entries.Add(new Entry("license", 2));

        Assert.Equal(["src", "b.cs", "a.cs", "c.cs", "readme", "license"], Rows(grid));
    }

    [Fact]
    public void TheRowsAreTheTreesOnly()
    {
        var (_, grid) = Show(Entries(out _, out _, out _, out _));

        Assert.Throws<InvalidOperationException>(() => grid.Items.Add(new Entry("x", 0)));
        Assert.Throws<InvalidOperationException>(() => grid.DataSource = new List<Entry>());
    }

    private sealed class TextRecorder : HeadlessGraphics
    {
        public List<(string Text, Rectangle Rect)> Texts { get; } = [];

        public override void DrawText(string text, Rectangle rect, Color color, Font font,
            HorizontalContentAlignment horizontal, VerticalContentAlignment vertical) => Texts.Add((text, rect));
    }

    [Fact]
    public void TheTreesColumnIsIndentedByDepth()
    {
        var (_, grid) = Show(Entries(out _, out _, out _, out _));
        grid.NodeAt(0)!.Expand();

        var g = new TextRecorder();
        grid.Draw(g);

        float top = g.Texts.Single(t => t.Text == "src").Rect.X;
        float child = g.Texts.Single(t => t.Text == "b.cs").Rect.X;
        float size = g.Texts.Single(t => t.Text == "20").Rect.X;
        float otherSize = g.Texts.Single(t => t.Text == "30").Rect.X;

        Assert.Equal(top + grid.Indent, child, 0.5f);

        // the other columns stand where they do in any grid
        Assert.Equal(otherSize, size, 0.5f);
    }
}
