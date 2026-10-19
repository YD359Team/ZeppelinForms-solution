using Xunit;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.DataGrid;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Controls.Tree;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>0.17.0: <c>ScrollPastEnd</c> — the scroll goes on past the end of the
/// content until the last row stands at the top, as in an editor. One rule for every
/// control that scrolls: panels and lists, the tree, the grid, the text box.</summary>
[Collection("Platform")]
public class ScrollPastEndTests
{
    private static Form Show(UIElement content, float width = 300, float height = 200)
    {
        var form = new Form { Size = new Size(width, height), Content = content };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();
        return form;
    }

    private static StackPanel TallPanel() => new()
    {
        OverflowY = Overflow.Auto,
        WheelStep = 20f,
        Children = { new Panel { Size = new Size(200, 1000) } },
    };

    [Fact]
    public void APanelStopsAtTheEndOfItsContentByDefault()
    {
        StackPanel panel = TallPanel();
        Show(panel);

        panel.ScrollTo(0, float.MaxValue);

        Assert.Equal(1000 - panel.ActualSize.Height, panel.ScrollY, 0.5f);
    }

    [Fact]
    public void APanelScrollsPastTheEndUntilTheLastRowIsAtTheTop()
    {
        StackPanel panel = TallPanel();
        panel.ScrollPastEnd = true;
        Show(panel);

        panel.ScrollTo(0, float.MaxValue);

        // the content's end stands one wheel step below the top
        Assert.Equal(1000 - 20f, panel.ScrollY, 0.5f);
    }

    [Fact]
    public void TurningItOffBringsTheScrollBackWithinTheContent()
    {
        StackPanel panel = TallPanel();
        panel.ScrollPastEnd = true;
        Show(panel);
        panel.ScrollTo(0, float.MaxValue);

        panel.ScrollPastEnd = false;

        Assert.Equal(1000 - panel.ActualSize.Height, panel.ScrollY, 0.5f);
    }

    [Fact]
    public void ATreeScrollsItsRowsPastTheEndByRows()
    {
        var tree = new TreeView { ItemHeight = 20f, ScrollPastEnd = true };

        for (int i = 0; i < 50; i++) tree.Nodes.Add(new TreeNode($"node {i}"));

        Show(tree);

        // the rows scroll in the tree's own panel
        var rows = Assert.IsType<VirtualizingStackPanel>(tree.Children[0]);
        rows.ScrollTo(0, float.MaxValue);

        Assert.True(rows.ScrollPastEnd);
        Assert.Equal(50 * 20f - 20f, rows.ScrollY, 0.5f);
    }

    [Fact]
    public void AGridScrollsPastItsLastRow()
    {
        var grid = new DataGridView
        {
            RowHeight = 25f,
            HeaderHeight = 30f,
            ScrollPastEnd = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Columns = { new DataGridViewColumn { Header = "N", Value = item => item } },
        };

        for (int i = 0; i < 40; i++) grid.Items.Add(i);

        Form form = Show(grid);

        grid.ScrollTo(0, float.MaxValue);
        form.UpdateLayout();

        // the last row stands right under the header
        Assert.Equal(40 * 25f - 25f, grid.ScrollState.Offset.Y, 0.5f);
    }

    [Fact]
    public void ATextBoxScrollsPastItsLastLine()
    {
        var box = new TextBox
        {
            IsMultiline = true,
            ScrollPastEnd = true,
            Text = string.Join("\n", Enumerable.Range(1, 40).Select(i => $"line {i}")),
            Font = Font.Default.WithSize(10),
            Size = new Size(200, 100),
        };

        Show(box);
        box.Draw(new HeadlessGraphics());

        box.ScrollTo(0, float.MaxValue);
        float pastEnd = box.VerticalOffset;

        box.ScrollPastEnd = false;
        box.ScrollTo(0, float.MaxValue);
        float atEnd = box.VerticalOffset;

        // the difference is the field's height but one line
        Assert.True(pastEnd > atEnd + 40, $"past the end {pastEnd}, at the end {atEnd}");
    }
}
