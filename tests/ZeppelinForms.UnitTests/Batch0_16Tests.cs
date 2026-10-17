using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Layout;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;
using ZeppelinForms.Theming;

namespace ZeppelinForms.UnitTests;

[Collection("Platform")]
public class Batch0_16Tests
{
    private static Form Host(UIElement content, Size size)
    {
        var platform = new HeadlessPlatform();
        var form = new Form { Size = size, Content = content };

        platform.CreateWindow(form);
        form.UpdateLayout();

        return form;
    }

    [Fact]
    public void WindowIsClearedWithTheThemesBackground()
    {
        Theme previous = App.Theme;

        try
        {
            App.Theme = Themes.Dark;
            var form = new Form();

            Assert.Equal(Themes.Dark.Colors.Background, form.EffectiveBackground);

            form.Background = Colors.Red;
            Assert.Equal(Colors.Red, form.EffectiveBackground);
        }
        finally
        {
            App.Theme = previous;
        }
    }

    [Fact]
    public void FlyoutWithoutRoomBelowOpensAbove()
    {
        var anchor = new Button { Text = "якорь" };

        var page = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Children = { new Border { Size = new Size(10, 260) }, anchor },
        };

        Form form = Host(page, new Size(400, 300));

        var content = new Border { Size = new Size(200, 150) };
        form.ShowFlyout(anchor, content, FlyoutPlacement.Bottom);

        float anchorTop = anchor.GetAbsolutePosition().Y;

        Assert.True(content.Position.Y + 150 <= anchorTop + 0.5f, $"flyout at {content.Position.Y}, anchor at {anchorTop}");
    }

    [Fact]
    public void RightClickKeepsTheSelectionAndOffersTheEditingMenu()
    {
        var box = new TextBox { Text = "привет мир", Size = new Size(200, 32) };
        Form form = Host(box, new Size(300, 100));

        box.SelectAll();

        Point inside = box.GetAbsolutePosition();
        inside = new Point(inside.X + 20, inside.Y + 16);

        form.OnPointerDown(inside, MouseButton.Right);
        form.OnPointerUp(inside, MouseButton.Right);

        Assert.Equal("привет мир", box.SelectedText);

        form.OnContextMenu(inside);

        MenuList menu = Assert.IsType<MenuList>(Assert.Single(form.Overlays, o => o is MenuList));
        Assert.Contains(menu.Items, item => !item.IsSeparator && item.IsEnabled);
    }

    [Fact]
    public void TwoPicksMakeARangeInEitherOrder()
    {
        var calendar = new Calendar { SelectionMode = CalendarSelectionMode.Range };
        int completed = 0;

        calendar.RangeSelected += (_, _) => completed++;
        calendar.SetSelectedDate(new DateTime(2026, 10, 8));

        // the later end first: the range is ordered all the same
        calendar.HandleNavigationKey(Key.Enter, KeyModifiers.None);

        for (int i = 0; i < 3; i++)
            calendar.HandleNavigationKey(Key.Left, KeyModifiers.None);

        calendar.HandleNavigationKey(Key.Enter, KeyModifiers.None);

        Assert.Equal(1, completed);
        Assert.Equal(new DateTime(2026, 10, 5), calendar.RangeStart);
        Assert.Equal(new DateTime(2026, 10, 8), calendar.RangeEnd);
    }

    [Fact]
    public void EscapeDropsARangeHalfPicked()
    {
        var calendar = new Calendar { SelectionMode = CalendarSelectionMode.Range };

        calendar.SetSelectedRange(new DateTime(2026, 10, 1), new DateTime(2026, 10, 3));
        calendar.HandleNavigationKey(Key.Enter, KeyModifiers.None);

        Assert.True(calendar.HandleNavigationKey(Key.Escape, KeyModifiers.None));

        // the range picked before stays
        Assert.Equal(new DateTime(2026, 10, 1), calendar.RangeStart);
        Assert.Equal(new DateTime(2026, 10, 3), calendar.RangeEnd);
    }
}