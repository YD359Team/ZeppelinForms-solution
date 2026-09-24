using Xunit;
using ZeppelinForms.Animation;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls.Tree;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.UnitTests;

[Collection("Platform")]
public class TreeKeyboardTests
{
    private static (Form Form, TreeView Tree) CreateTree()
    {
        var platform = new HeadlessPlatform();

        var tree = new TreeView
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        for (int i = 0; i < 3; i++)
        {
            var node = new TreeNode($"узел {i}");

            node.Children.Add(new TreeNode($"потомок {i}.0"));
            node.Children.Add(new TreeNode($"потомок {i}.1"));

            tree.Nodes.Add(node);
        }

        var form = new Form { Size = new Size(400, 300), Content = tree };
        platform.CreateWindow(form);
        form.UpdateLayout();

        // клик по строке отдаёт фокус дереву — без него клавиатуры нет
        HeadlessInput.Click(form, 100, 12);

        return (form, tree);
    }

    [Fact]
    public void ClickFocusesTreeThroughRow()
    {
        var (_, tree) = CreateTree();

        Assert.True(tree.IsFocused);
    }

    [Fact]
    public void ArrowsMoveSelection()
    {
        var (form, tree) = CreateTree();

        Assert.Same(tree.Nodes[0], tree.SelectedNode);

        HeadlessInput.PressKey(form, Key.Down);
        Assert.Same(tree.Nodes[1], tree.SelectedNode);

        HeadlessInput.PressKey(form, Key.Up);
        Assert.Same(tree.Nodes[0], tree.SelectedNode);

        HeadlessInput.PressKey(form, Key.End);
        Assert.Same(tree.Nodes[2], tree.SelectedNode);

        HeadlessInput.PressKey(form, Key.Home);
        Assert.Same(tree.Nodes[0], tree.SelectedNode);
    }

    [Fact]
    public void RightExpandsThenEnters()
    {
        var (form, tree) = CreateTree();

        HeadlessInput.PressKey(form, Key.Right);
        form.UpdateLayout();

        Assert.True(tree.Nodes[0].IsExpanded);
        Assert.Same(tree.Nodes[0], tree.SelectedNode);

        // раскрытый узел вторым нажатием пускает внутрь
        HeadlessInput.PressKey(form, Key.Right);

        Assert.Same(tree.Nodes[0].Children[0], tree.SelectedNode);
    }

    [Fact]
    public void LeftCollapsesThenGoesToParent()
    {
        var (form, tree) = CreateTree();

        tree.Nodes[0].Expand();
        form.UpdateLayout();

        HeadlessInput.PressKey(form, Key.Down);
        Assert.Same(tree.Nodes[0].Children[0], tree.SelectedNode);

        // у потомка сворачивать нечего — уходим к родителю
        HeadlessInput.PressKey(form, Key.Left);
        Assert.Same(tree.Nodes[0], tree.SelectedNode);

        HeadlessInput.PressKey(form, Key.Left);
        Assert.False(tree.Nodes[0].IsExpanded);
    }

    [Fact]
    public void EnterTogglesNode()
    {
        var (form, tree) = CreateTree();

        HeadlessInput.PressKey(form, Key.Enter);
        form.UpdateLayout();

        Assert.True(tree.Nodes[0].IsExpanded);

        HeadlessInput.PressKey(form, Key.Enter);
        form.UpdateLayout();

        Assert.False(tree.Nodes[0].IsExpanded);
    }

    [Fact]
    public void SelectionScrollsIntoView()
    {
        var platform = new HeadlessPlatform();

        var tree = new TreeView
        {
            ItemHeight = 24f,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        for (int i = 0; i < 100; i++)
            tree.Nodes.Add(new TreeNode($"узел {i}"));

        var form = new Form { Size = new Size(400, 300), Content = tree };
        platform.CreateWindow(form);
        form.UpdateLayout();

        HeadlessInput.Click(form, 100, 12);
        HeadlessInput.PressKey(form, Key.End);
        form.UpdateLayout();

        var rows = tree.Children.OfType<ZeppelinForms.Forms.Controls.VirtualizingStackPanel>().Single();

        // последняя строка должна оказаться видимой, а не остаться за краем
        Assert.True(rows.ScrollY > 0f);
        Assert.Contains(
            rows.Children.OfType<TreeViewItem>(),
            row => ReferenceEquals(row.Node, tree.Nodes[99]));
    }

    [Fact]
    public void ShortMoveIsFasterThanLongOne()
    {
        // длительность переезда зависит от расстояния: строка, уехавшая
        // на высоту строки, не должна ползти столько же, сколько уехавшая
        // через весь экран
        var rule = LayoutTransition.Speed(pixelsPerSecond: 1600f, minDurationMs: 90, maxDurationMs: 320);

        Transition near = rule.For(ZeppelinForms.Forms.Controls.Base.UIElement.TranslateYProperty, 24f);
        Transition far = rule.For(ZeppelinForms.Forms.Controls.Base.UIElement.TranslateYProperty, 600f);

        Assert.True(near.Duration < far.Duration);
        Assert.Equal(TimeSpan.FromMilliseconds(90), near.Duration);
        Assert.Equal(TimeSpan.FromMilliseconds(320), far.Duration);
    }
}