using Xunit;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.UnitTests;

/// <summary>0.17.0: a list box makes containers only for the rows in sight.</summary>
[Collection("Platform")]
public class ListBoxVirtualizationTests
{
    private static (Form Form, ListBox List) Show(int count, bool virtualizing = true)
    {
        Keyboard.Reset();

        var list = new ListBox
        {
            IsVirtualizing = virtualizing,
            ItemHeight = 20f,
            OverflowY = Overflow.Auto,
            Size = new Size(200, 200),
        };

        for (int i = 0; i < count; i++) list.Items.Add($"item {i}");

        var form = new Form { Size = new Size(400, 400), Content = list };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return (form, list);
    }

    private static string TextOf(UIElement? container) => Assert.IsType<Label>(container).Text!;

    [Fact]
    public void ALongListMakesContainersOnlyForTheRowsInSight()
    {
        (_, ListBox list) = Show(10_000);

        // ten rows in sight and a few around them
        Assert.InRange(list.Children.Count, 10, 20);
        Assert.Equal("item 0", TextOf(list.ContainerFromIndex(0)));
        Assert.Null(list.ContainerFromIndex(5_000));
    }

    [Fact]
    public void TheScrollRangeIsTheWholeList()
    {
        (Form form, ListBox list) = Show(10_000);

        list.ScrollTo(0, float.MaxValue);
        form.UpdateLayout();

        Assert.Equal("item 9999", TextOf(list.ContainerFromIndex(9_999)));
        Assert.Null(list.ContainerFromIndex(0));
        Assert.InRange(list.Children.Count, 10, 20);
    }

    [Fact]
    public void ContainerAndIndexMapBothWays()
    {
        (Form form, ListBox list) = Show(1_000);

        list.ScrollTo(0, 500 * 20f);
        form.UpdateLayout();

        UIElement container = Assert.IsAssignableFrom<UIElement>(list.ContainerFromIndex(505));
        Assert.Equal(505, list.IndexFromContainer(container));
        Assert.Equal("item 505", TextOf(container));
        Assert.Equal(-1, list.IndexFromContainer(new Label()));
    }

    [Fact]
    public void EndSelectsTheLastRowAndScrollsItIntoView()
    {
        (Form form, ListBox list) = Show(1_000);
        form.FocusElement(list);
        list.SelectedIndex = 0;

        HeadlessInput.PressKey(form, Key.End);
        form.UpdateLayout();

        Assert.Equal(999, list.SelectedIndex);
        Assert.Equal("item 999", TextOf(list.ContainerFromIndex(999)));
    }

    [Fact]
    public void ScrollIntoViewDoesNothingForARowInSight()
    {
        (_, ListBox list) = Show(1_000);

        list.ScrollIntoView(3);

        Assert.Equal(0, list.ScrollY);
    }

    [Fact]
    public void TheSelectedTextColorFollowsTheItemNotTheReusedLabel()
    {
        (Form form, ListBox list) = Show(1_000);
        list.SelectedTextColor = Colors.Red;
        list.SelectedIndex = 1;
        form.UpdateLayout();

        Assert.Equal(Colors.Red, list.ContainerFromIndex(1)!.TextColor);

        // the label of row 1 goes on to show another item
        list.ScrollTo(0, 500 * 20f);
        form.UpdateLayout();

        foreach (UIElement row in list.Children)
            Assert.NotEqual(Colors.Red, row.TextColor);

        list.ScrollTo(0, 0);
        form.UpdateLayout();

        Assert.Equal(Colors.Red, list.ContainerFromIndex(1)!.TextColor);
    }

    [Fact]
    public void WithoutVirtualizationEveryItemHasAContainer()
    {
        (_, ListBox list) = Show(100, virtualizing: false);

        Assert.Equal(100, list.Children.Count);
        Assert.Equal("item 99", TextOf(list.ContainerFromIndex(99)));
        Assert.Equal(42, list.IndexFromContainer(list.Children[42]));
    }

    [Fact]
    public void TurningVirtualizationOffAndOnKeepsTheRows()
    {
        (Form form, ListBox list) = Show(100);

        list.IsVirtualizing = false;
        form.UpdateLayout();
        Assert.Equal(100, list.Children.Count);

        list.IsVirtualizing = true;
        form.UpdateLayout();
        Assert.InRange(list.Children.Count, 10, 20);
        Assert.Equal("item 0", TextOf(list.ContainerFromIndex(0)));
    }

    [Fact]
    public void AnItemAddedInSightGetsARow()
    {
        (Form form, ListBox list) = Show(5);

        list.Items.Insert(0, "first");
        form.UpdateLayout();

        Assert.Equal("first", TextOf(list.ContainerFromIndex(0)));
        Assert.Equal("item 4", TextOf(list.ContainerFromIndex(5)));
        Assert.Equal(6, list.Children.Count);
    }

    [Fact]
    public void ANegativeItemHeightIsRefused()
    {
        var list = new ListBox();

        Assert.Throws<ArgumentOutOfRangeException>(() => list.ItemHeight = -1);
        list.ItemHeight = float.NaN;
    }
}
