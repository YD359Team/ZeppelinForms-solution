using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Controls.Tree;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

[Collection("Platform")]
public class VirtualizationTests
{
    private static (Form Form, VirtualizingStackPanel Panel) CreateList(int itemCount, Size formSize)
    {
        var panel = new VirtualizingStackPanel
        {
            ItemHeight = 20,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        for (int i = 0; i < itemCount; i++)
            panel.ItemsSource.Add(i.ToString());

        var form = new Form { Size = formSize, Content = panel };
        new HeadlessPlatform().CreateWindow(form);

        return (form, panel);
    }

    private static IEnumerable<string> RealizedTexts(VirtualizingStackPanel panel) =>
        panel.Children.OfType<Label>().Select(label => label.Text)!;

    [Fact]
    public void ScrollRealizesRowsInsideViewport()
    {
        var (_, panel) = CreateList(1000, new Size(400, 300));

        // 5000 / 20 = строка 250 — первая видимая после прокрутки
        panel.ScrollTo(0, 5000);

        string[] texts = [.. RealizedTexts(panel)];

        Assert.Contains("250", texts);
        Assert.Contains("260", texts);
        Assert.DoesNotContain("0", texts);
    }

    [Fact]
    public void TallViewportIsFilledCompletely()
    {
        // раньше измерение брало двадцать строк вместо высоты окна,
        // и нижняя половина высокого окна оставалась пустой
        var (_, panel) = CreateList(1000, new Size(400, 1000));

        string[] texts = [.. RealizedTexts(panel)];

        Assert.Contains("49", texts);
    }

    [Fact]
    public void CollapsingWhileScrolledKeepsRowsVisible()
    {
        var (_, panel) = CreateList(1000, new Size(400, 300));

        panel.ScrollTo(0, 19000);

        // список сжимается под текущей прокруткой
        while (panel.ItemsSource.Count > 10)
            panel.ItemsSource.RemoveAt(panel.ItemsSource.Count - 1);

        panel.Refresh();

        string[] texts = [.. RealizedTexts(panel)];

        Assert.Equal(10, texts.Length);
        Assert.Contains("0", texts);
    }

    [Fact]
    public void ExpandingNodeKeepsExistingRows()
    {
        var tree = new TreeView
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        for (int i = 0; i < 100; i++)
        {
            var node = new TreeNode($"node {i}");
            node.Children.Add(new TreeNode($"child {i}"));
            tree.Nodes.Add(node);
        }

        var form = new Form { Size = new Size(400, 300), Content = tree };
        new HeadlessPlatform().CreateWindow(form);

        var panel = tree.Children.OfType<VirtualizingStackPanel>().Single();

        TreeViewItem RowOf(TreeNode node) =>
            panel.Children.OfType<TreeViewItem>().Single(row => ReferenceEquals(row.Node, node));

        TreeViewItem before = RowOf(tree.Nodes[1]);

        tree.Nodes[0].IsExpanded = true;

        // строка соседнего узла — тот же объект, только ниже на одну позицию
        Assert.Same(before, RowOf(tree.Nodes[1]));

        // и у раскрытого узла появилась строка потомка
        Assert.Contains(panel.Children.OfType<TreeViewItem>(), row => ReferenceEquals(row.Node, tree.Nodes[0].Children[0]));

        // ни одна строка не показывает узел дважды
        Assert.Equal(
            panel.Children.Count,
            panel.Children.OfType<TreeViewItem>().Select(row => row.Node).Distinct().Count());
    }
}