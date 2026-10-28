using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.UnitTests;

/// <summary>0.19: a context menu's submenus — opened by a click, a resting pointer, the
/// keyboard — and a finger held on an element opening its menu.</summary>
[Collection("Platform")]
public class ContextMenuTests
{
    private sealed class Setup
    {
        public required Form Form { get; init; }
        public required HeadlessPlatform Platform { get; init; }
        public required Border Area { get; init; }
        public required List<string> Chosen { get; init; }

        /// <summary>A point inside the area: the panel centers it.</summary>
        public Point At => new(Area.GetAbsolutePosition().X + 20, Area.GetAbsolutePosition().Y + 20);
    }

    private static Setup Make()
    {
        var chosen = new List<string>();

        MenuItem Item(string text)
        {
            var item = new MenuItem { Text = text };
            item.Click += (_, _) => chosen.Add(text);
            return item;
        }

        var share = new MenuItem { Text = "Share" };
        share.Items.AddRange([Item("E-mail"), Item("Link")]);

        var area = new Border
        {
            Size = new Size(300, 100),

            // an element without a fill lets the pointer through to what is under it
            Background = new Color(255, 240, 240, 240),
            ContextMenu = [Item("Copy"), Item("Paste"), MenuItem.Separator, share],
        };

        var platform = new HeadlessPlatform();
        var form = new Form { Size = new Size(640, 480), Content = new StackPanel { Children = { area } } };

        platform.CreateWindow(form);
        form.UpdateLayout();

        return new Setup { Form = form, Platform = platform, Area = area, Chosen = chosen };
    }

    private static List<MenuList> Menus(Form form) => [.. form.Overlays.OfType<MenuList>()];

    /// <summary>The middle of an item's row, in the form's coordinates.</summary>
    private static Point Row(MenuList menu, int index)
    {
        // a menu just opened has its place, and its size after the layout
        menu.FindOwner()?.UpdateLayout();

        Rectangle bounds = menu.ItemBounds(index);
        return new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
    }

    private static void Pump(HeadlessPlatform platform, Func<bool> until)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);

        while (!until() && DateTime.UtcNow < deadline)
        {
            platform.PumpAll();
            Thread.Sleep(5);
        }
    }

    [Fact]
    public void AClickOnAnItemWithASubmenuOpensItAndAChoiceInItRuns()
    {
        Setup s = Make();

        HeadlessInput.RightClick(s.Form, s.At.X, s.At.Y);
        MenuList menu = Assert.Single(Menus(s.Form));

        Point share = Row(menu, 3);
        HeadlessInput.Click(s.Form, share.X, share.Y);

        // the menu stays, its submenu beside it
        Assert.Equal(2, Menus(s.Form).Count);
        Assert.Empty(s.Chosen);

        MenuList submenu = Menus(s.Form)[1];
        Assert.True(submenu.Position.X >= menu.Position.X + menu.ActualSize.Width - 4);

        Point email = Row(submenu, 0);
        HeadlessInput.Click(s.Form, email.X, email.Y);

        Assert.Equal(["E-mail"], s.Chosen);
        Assert.Empty(Menus(s.Form));
    }

    [Fact]
    public void TheKeyboardGoesIntoASubmenuAndBack()
    {
        Setup s = Make();

        HeadlessInput.RightClick(s.Form, s.At.X, s.At.Y);

        // Copy, Paste, (separator), Share
        HeadlessInput.PressKey(s.Form, Key.Down);
        HeadlessInput.PressKey(s.Form, Key.Down);
        HeadlessInput.PressKey(s.Form, Key.Down);
        HeadlessInput.PressKey(s.Form, Key.Right);

        Assert.Equal(2, Menus(s.Form).Count);

        HeadlessInput.PressKey(s.Form, Key.Left);
        Assert.Single(Menus(s.Form));

        HeadlessInput.PressKey(s.Form, Key.Enter);
        Assert.Equal(2, Menus(s.Form).Count);

        // the submenu's first item is lit: Down goes to the second
        HeadlessInput.PressKey(s.Form, Key.Down);
        HeadlessInput.PressKey(s.Form, Key.Enter);

        Assert.Equal(["Link"], s.Chosen);
        Assert.Empty(Menus(s.Form));
    }

    [Fact]
    public void ARestingPointerOpensTheSubmenuAndAnotherItemClosesIt()
    {
        Setup s = Make();

        HeadlessInput.RightClick(s.Form, s.At.X, s.At.Y);
        MenuList menu = Assert.Single(Menus(s.Form));

        Point share = Row(menu, 3);
        HeadlessInput.MoveMouse(s.Form, share.X, share.Y);

        // not at once: on the way to a submenu the pointer crosses other items
        Assert.Single(Menus(s.Form));

        Pump(s.Platform, () => Menus(s.Form).Count == 2);
        Assert.Equal(2, Menus(s.Form).Count);

        Point copy = Row(menu, 0);
        HeadlessInput.MoveMouse(s.Form, copy.X, copy.Y);

        Pump(s.Platform, () => Menus(s.Form).Count == 1);
        Assert.Single(Menus(s.Form));
    }

    [Fact]
    public void AFingerHeldOnAnElementOpensItsMenu()
    {
        Setup s = Make();

        HeadlessInput.TouchDown(s.Form, 0, s.At.X, s.At.Y);

        Pump(s.Platform, () => Menus(s.Form).Count == 1);
        Assert.Single(Menus(s.Form));

        // the release that ends the hold is no tap
        HeadlessInput.TouchUp(s.Form, 0, s.At.X, s.At.Y);
        Assert.Single(Menus(s.Form));
        Assert.Empty(s.Chosen);
    }

    [Fact]
    public void AShortTouchOpensNoMenu()
    {
        Setup s = Make();

        HeadlessInput.Tap(s.Form, s.At.X, s.At.Y);

        Thread.Sleep(50);
        s.Platform.PumpAll();

        Assert.Empty(Menus(s.Form));
    }
}
