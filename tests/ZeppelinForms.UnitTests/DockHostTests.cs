using System.Globalization;
using Xunit;
using ZeppelinForms.Core.Globalization;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Docking;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.UnitTests;

/// <summary>0.20: panels docked, split, tabbed, floated inside the host, pinned to its
/// edges, dragged by the user onto the compass, and saved and restored as JSON.</summary>
[Collection("Platform")]
public class DockHostTests
{
    private static (Form Form, DockHost Host) Show(DockHost host)
    {
        var form = new Form { Size = new Size(800, 600), Content = host };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return (form, host);
    }

    private static DockItem Item(string id) => new(id, id, new Label { Text = id });

    /// <summary>An editor in the middle, the files on the left, the output at the bottom.</summary>
    private static (DockHost Host, DockItem Editor, DockItem Readme, DockItem Files, DockItem Output) Ide()
    {
        var host = new DockHost();

        DockItem editor = Item("editor");
        DockItem readme = Item("readme");
        DockItem files = Item("files");
        DockItem output = Item("output");

        host.DockTo(editor);
        host.DockTo(readme, DockPosition.Tabbed, editor);
        host.DockTo(files, DockPosition.Left, size: 0.25f);
        host.DockTo(output, DockPosition.Bottom, editor, 0.3f);

        return (host, editor, readme, files, output);
    }

    /// <summary>Where an item's content is, in the form's coordinates.</summary>
    private static Rectangle BoundsOf(DockItem item)
    {
        UIElement content = item.Content!;
        Point at = content.GetAbsolutePosition();

        return new Rectangle(at, content.ActualSize);
    }

    private static bool IsShown(DockItem item) => item.Content!.FindOwner() is not null;

    // ===== placing =====

    [Fact]
    public void ItemsDockBesideAndAsTabs()
    {
        var (host, editor, readme, files, output) = Ide();
        var (form, _) = Show(host);

        Assert.All([editor, readme, files, output], item => Assert.Equal(DockState.Docked, item.State));
        Assert.Same(editor.Group, readme.Group);

        // the tab added last is selected; the other one is out of the tree
        Assert.True(IsShown(readme));
        Assert.False(IsShown(editor));

        Rectangle filesBox = BoundsOf(files);
        Rectangle readmeBox = BoundsOf(readme);
        Rectangle outputBox = BoundsOf(output);

        Assert.True(filesBox.Right <= readmeBox.X, "the files are left of the editor");
        Assert.InRange(filesBox.Width, 800 * 0.2f, 800 * 0.3f);
        Assert.True(readmeBox.Bottom <= outputBox.Y, "the output is under the editor");
        Assert.True(outputBox.X >= filesBox.Right, "the output is under the editor alone");

        host.Show(editor);
        form.UpdateLayout();

        Assert.True(IsShown(editor));
        Assert.False(IsShown(readme));
        Assert.Same(editor, host.ActiveItem);
    }

    [Fact]
    public void AnItemIsInOneHostAndItsIdIsItsOwn()
    {
        var host = new DockHost();
        DockItem item = Item("a");
        host.DockTo(item);

        Assert.Throws<ArgumentException>(() => host.DockTo(Item("a")));
        Assert.Throws<InvalidOperationException>(() => new DockHost().DockTo(item));
        Assert.Throws<ArgumentException>(() => host.DockTo(Item("b"), DockPosition.Left, Item("c")));
    }

    [Fact]
    public void AHiddenItemGoesBackWhereItWas()
    {
        var (host, editor, readme, files, output) = Ide();
        var (form, _) = Show(host);

        float left = BoundsOf(files).X;

        // the files' group was all there was on the left: the split gives its place up
        host.Hide(files);
        form.UpdateLayout();

        Assert.Equal(DockState.Hidden, files.State);
        Assert.False(IsShown(files));
        Assert.True(BoundsOf(readme).X < 10, "the editor took the left");
        Assert.Contains(files, host.Items);

        host.Show(files);
        form.UpdateLayout();

        Assert.Equal(DockState.Docked, files.State);
        Assert.Equal(left, BoundsOf(files).X, 1f);
        Assert.True(BoundsOf(files).Right <= BoundsOf(readme).X);

        // a tab of a group still there goes back into it
        host.Hide(editor);
        host.Show(editor);

        Assert.Same(readme.Group, editor.Group);
        _ = output;
    }

    /// <summary>A group gone from the layout holds nothing: a hidden item's content was
    /// left a child of the group's view, which the item kept alive by remembering the group.</summary>
    [Fact]
    public void AGoneGroupLetsItsContentGo()
    {
        var (host, _, _, files, _) = Ide();
        Show(host);

        host.Hide(files);

        Assert.Null(files.Content!.Parent);
        Assert.Null(files.LastGroup!.View);

        host.Show(files);
        Assert.True(IsShown(files));
    }

    [Fact]
    public void AFloatingItemIsAWindowInsideTheHostAndDocksBack()
    {
        var (host, editor, readme, _, _) = Ide();
        var (form, _) = Show(host);

        host.Float(readme, new Rectangle(100, 80, 300, 200));
        form.UpdateLayout();

        Assert.Equal(DockState.Floating, readme.State);
        Assert.True(IsShown(readme));

        Rectangle box = BoundsOf(readme);
        Point origin = host.GetAbsolutePosition();

        Assert.InRange(box.X - origin.X, 100, 110);
        Assert.InRange(box.Y - origin.Y, 100, 120);

        // the window lies over the layout: a press on it reaches its content
        Assert.Same(readme.Content, ZeppelinForms.Forms.Controls.Tools.HitTester.HitTest(form.Content!, box.Center));

        host.Show(readme);
        Assert.Equal(DockState.Floating, readme.State);

        // docked back into the group it came from
        host.DockTo(readme, DockPosition.Tabbed, editor);
        Assert.Equal(DockState.Docked, readme.State);
        Assert.Same(editor.Group, readme.Group);
    }

    [Fact]
    public void AnAutoHiddenItemSlidesOutFromItsEdge()
    {
        var (host, _, readme, files, _) = Ide();
        var (form, _) = Show(host);

        host.AutoHide(files);
        form.UpdateLayout();

        Assert.Equal(DockState.AutoHidden, files.State);
        Assert.Equal([files], host.AutoHiddenItems(DockSide.Left));
        Assert.False(IsShown(files));

        // the layout stands aside for the strip
        Point origin = host.GetAbsolutePosition();
        Assert.True(BoundsOf(readme).X - origin.X >= 24f, "the layout is right of the strip");

        // a click on the strip's button slides it out, over the layout
        var button = new Point(origin.X + 12, origin.Y + 30);
        form.OnPointerDown(button);
        form.OnPointerUp(button);
        form.UpdateLayout();

        Assert.True(IsShown(files));
        Assert.Same(files, host.ActiveItem);

        // a press elsewhere slides it back
        Point away = BoundsOf(readme).Center;
        form.OnPointerDown(new Point(origin.X + 700, away.Y));
        form.OnPointerUp(new Point(origin.X + 700, away.Y));
        form.UpdateLayout();

        Assert.False(IsShown(files));
        Assert.Equal(DockState.AutoHidden, files.State);

        // and docked back, on its side
        host.Show(files);
        Assert.True(IsShown(files));

        host.DockTo(files, DockPosition.Left);
        form.UpdateLayout();

        Assert.Equal(DockState.Docked, files.State);
        Assert.Empty(host.AutoHiddenItems(DockSide.Left));
    }

    [Fact]
    public void ClosingAsksAndHidesOrRemoves()
    {
        var (host, editor, readme, files, _) = Ide();
        Show(host);

        files.HideOnClose = true;

        List<string> closed = [];
        host.ItemClosed += (_, item) => closed.Add(item.Id);
        host.ItemClosing += (_, e) => e.Cancel = e.Item == editor;

        Assert.False(host.Close(editor));
        Assert.Equal(DockState.Docked, editor.State);

        Assert.True(host.Close(readme));
        Assert.DoesNotContain(readme, host.Items);
        Assert.Null(readme.Host);
        Assert.Null(readme.Content!.Parent);

        Assert.True(host.Close(files));
        Assert.Contains(files, host.Items);
        Assert.Equal(DockState.Hidden, files.State);

        Assert.Equal(["readme", "files"], closed);

        // a closed item is free to go elsewhere
        new DockHost().DockTo(readme);
    }

    // ===== the layout as JSON =====

    [Fact]
    public void ALayoutSavedIsReadBackAsItWas()
    {
        var (host, editor, readme, files, output) = Ide();
        Show(host);

        var search = Item("search");
        var props = Item("props");

        host.Float(search, new Rectangle(40, 30, 320, 240));
        host.AutoHide(output, DockSide.Bottom);
        host.DockTo(props, DockPosition.Right);
        host.Hide(props);
        host.Show(editor);

        string saved = host.SaveLayout();

        // the same items in another host, made by id
        var items = new[] { editor, readme, files, output, search, props }
            .ToDictionary(item => item.Id, item => Item(item.Id));

        var other = new DockHost();
        Show(other);
        other.LoadLayout(saved, id => items.GetValueOrDefault(id));

        Assert.Equal(saved, other.SaveLayout());

        Assert.Equal(DockState.Docked, items["files"].State);
        Assert.Equal(DockState.Floating, items["search"].State);
        Assert.Equal(DockState.AutoHidden, items["output"].State);
        Assert.Equal(DockState.Hidden, items["props"].State);
        Assert.Same(items["editor"], other.ActiveItem);
        Assert.Same(items["editor"].Group, items["readme"].Group);
    }

    [Fact]
    public void ALayoutReadIntoItsOwnHostPutsTheItemsBack()
    {
        var (host, editor, readme, files, _) = Ide();
        var (form, _) = Show(host);

        string saved = host.SaveLayout();
        Rectangle filesBox = BoundsOf(files);

        host.Float(files);
        host.DockTo(readme, DockPosition.Right, editor);
        host.AutoHide(editor);

        host.LoadLayout(saved);
        form.UpdateLayout();

        Assert.Equal(saved, host.SaveLayout());
        Assert.Equal(filesBox, BoundsOf(files));
        Assert.Same(editor.Group, readme.Group);
    }

    [Fact]
    public void NotALayoutChangesNothing()
    {
        var (host, _, _, _, _) = Ide();
        Show(host);

        string saved = host.SaveLayout();

        Assert.Throws<FormatException>(() => host.LoadLayout("{ not json"));
        Assert.Throws<FormatException>(() => host.LoadLayout("""{ "root": { "split": "diagonal" } }"""));
        Assert.Throws<FormatException>(() => host.LoadLayout("""{ "root": { "group": [1, 2] } }"""));

        Assert.Equal(saved, host.SaveLayout());
    }

    [Fact]
    public void ItemsTheLayoutDoesntNameAreHiddenAndUnknownIdsLeftOut()
    {
        var (host, editor, readme, files, output) = Ide();
        Show(host);

        host.LoadLayout("""{ "root": { "group": ["files", "nobody"], "selected": "files" } }""");

        Assert.Equal(DockState.Docked, files.State);
        Assert.All([editor, readme, output], item => Assert.Equal(DockState.Hidden, item.State));
        Assert.Equal(4, host.Items.Count);
    }

    // ===== the user's hands =====

    /// <summary>The middle of a tab of an item, in the form's coordinates.</summary>
    private static Point TabOf(DockItem item)
    {
        TabStrip strip = item.Group!.View!.Strip;
        int index = item.Group.Items.IndexOf(item);
        Rectangle tab = strip.TabRect(index);
        Point at = strip.GetAbsolutePosition();

        return new Point(at.X + tab.X + tab.Width / 2f, at.Y + tab.Y + tab.Height / 2f);
    }

    private static Point ToForm(DockHost host, Rectangle local)
    {
        Point at = host.GetAbsolutePosition();
        return new Point(at.X + local.Center.X, at.Y + local.Center.Y);
    }

    [Fact]
    public void ATabDraggedOntoTheCompassDocksThere()
    {
        var (host, editor, readme, files, _) = Ide();
        var (form, _) = Show(host);

        Point tab = TabOf(readme);

        form.OnPointerDown(tab);
        form.OnPointerMove(new Point(tab.X + 2, tab.Y + 30));
        form.OnPointerMove(new Point(tab.X + 4, tab.Y + 60));

        Assert.True(host.IsDragging, "the tab left its strip and the host took the drag");

        // over the files' group: its compass is there
        Point over = BoundsOf(files).Center;
        form.OnPointerMove(over);

        DockHost.DockTarget below = host.DragTargets.Single(t => t.Group == files.Group && t.Position == DockPosition.Bottom);
        Point button = ToForm(host, below.Button);

        form.OnPointerMove(button);
        form.OnPointerUp(button);
        form.UpdateLayout();

        Assert.False(host.IsDragging);
        Assert.Equal(DockState.Docked, readme.State);
        Assert.NotSame(editor.Group, readme.Group);

        Rectangle filesBox = BoundsOf(files);
        Rectangle readmeBox = BoundsOf(readme);

        Assert.True(readmeBox.Y >= filesBox.Bottom, "the readme is under the files");
        Assert.Equal(filesBox.X, readmeBox.X, 1f);
    }

    [Fact]
    public void ATabDroppedAnywhereElseFloatsWhereItWasDropped()
    {
        var (host, _, readme, _, _) = Ide();
        var (form, _) = Show(host);

        Point tab = TabOf(readme);
        Point origin = host.GetAbsolutePosition();

        // a spot away from every compass button
        var drop = new Point(origin.X + 560, origin.Y + 120);

        form.OnPointerDown(tab);
        form.OnPointerMove(new Point(tab.X, tab.Y + 60));
        form.OnPointerMove(drop);
        form.OnPointerUp(drop);
        form.UpdateLayout();

        Assert.Equal(DockState.Floating, readme.State);

        Rectangle box = BoundsOf(readme);
        Assert.True(box.X < drop.X && box.Right > drop.X, "the window is where it was dropped");
    }

    [Fact]
    public void EscCancelsADrag()
    {
        var (host, editor, readme, _, _) = Ide();
        var (form, _) = Show(host);

        Point tab = TabOf(readme);

        form.OnPointerDown(tab);
        form.OnPointerMove(new Point(tab.X, tab.Y + 60));
        Assert.True(host.IsDragging);

        form.OnKeyDown(Key.Escape, KeyModifiers.None, false);
        Assert.False(host.IsDragging);

        form.OnPointerUp(new Point(tab.X, tab.Y + 60));

        Assert.Equal(DockState.Docked, readme.State);
        Assert.Same(editor.Group, readme.Group);
    }

    [Fact]
    public void AWindowDraggedByItsTitleOntoAGroupBecomesItsTab()
    {
        var (host, editor, readme, files, _) = Ide();
        var (form, _) = Show(host);

        host.Float(readme, new Rectangle(300, 200, 260, 180));
        form.UpdateLayout();

        Point origin = host.GetAbsolutePosition();
        var title = new Point(origin.X + 340, origin.Y + 210);

        form.OnPointerDown(title);
        form.OnPointerMove(new Point(title.X - 40, title.Y + 10));
        Assert.True(host.IsDragging);

        form.OnPointerMove(BoundsOf(files).Center);

        DockHost.DockTarget middle = host.DragTargets.Single(t => t.Group == files.Group && t.Position == DockPosition.Tabbed);
        Point button = ToForm(host, middle.Button);

        form.OnPointerMove(button);
        form.OnPointerUp(button);

        Assert.Equal(DockState.Docked, readme.State);
        Assert.Same(files.Group, readme.Group);
        _ = editor;
    }

    [Fact]
    public void ASplitterDragMovesTheShareAndTellsOfIt()
    {
        var (host, _, readme, files, _) = Ide();
        var (form, _) = Show(host);

        int changes = 0;
        host.LayoutChanged += (_, _) => changes++;

        Rectangle filesBox = BoundsOf(files);
        var gap = new Point(filesBox.Right + 2, filesBox.Center.Y);

        form.OnPointerDown(gap);
        form.OnPointerMove(new Point(gap.X + 50, gap.Y));
        form.OnPointerMove(new Point(gap.X + 100, gap.Y));
        form.OnPointerUp(new Point(gap.X + 100, gap.Y));
        form.UpdateLayout();

        Assert.Equal(filesBox.Width + 100, BoundsOf(files).Width, 2f);
        Assert.True(BoundsOf(readme).X > filesBox.Right + 90);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void ThePinOfAGroupSlidesAllOfItAwayAndTheyComeBackTogether()
    {
        var (host, editor, readme, files, _) = Ide();
        var (form, _) = Show(host);

        // the editor's group is in the middle: its pin is at the right end of its header
        DockGroupView view = readme.Group!.View!;
        Point at = view.GetAbsolutePosition();
        float header = view.Strip.ActualSize.Height;
        var pin = new Point(at.X + view.ActualSize.Width - header / 2f, at.Y + header / 2f);

        form.OnPointerDown(pin);
        form.OnPointerUp(pin);
        form.UpdateLayout();

        Assert.Equal(DockState.AutoHidden, editor.State);
        Assert.Equal(DockState.AutoHidden, readme.State);
        Assert.Equal(editor.Side, readme.Side);

        // docked back one by one, as the pins of their panels do: the group they left is
        // gone, and the second joins the first where it went
        host.Redock(editor);
        host.Redock(readme);
        form.UpdateLayout();

        Assert.Equal(DockState.Docked, readme.State);
        Assert.Same(editor.Group, readme.Group);

        // the group's middle was nearer the top than the right: the top it was pinned to,
        // and the top it docks back on, over the whole layout
        Assert.Equal(DockSide.Top, readme.Side);
        Assert.True(BoundsOf(readme).Bottom <= BoundsOf(files).Y, "back on its side of the layout");
    }

    [Fact]
    public void AWindowIsResizedByItsFrameAndKeptWithinReach()
    {
        var (host, _, readme, _, _) = Ide();
        var (form, _) = Show(host);

        host.Float(readme, new Rectangle(200, 150, 300, 200));
        form.UpdateLayout();

        Point origin = host.GetAbsolutePosition();
        var corner = new Point(origin.X + 200 + 300 - 2, origin.Y + 150 + 200 - 2);

        form.OnPointerDown(corner);
        form.OnPointerMove(new Point(corner.X + 40, corner.Y + 30));
        form.OnPointerUp(new Point(corner.X + 40, corner.Y + 30));
        form.UpdateLayout();

        Rectangle window = readme.Group!.Float!.Bounds;
        Assert.Equal(340f, window.Width, 1f);
        Assert.Equal(230f, window.Height, 1f);

        // a window put far away keeps its title bar in the host
        host.Float(readme, new Rectangle(5000, -300, 300, 200));
        form.UpdateLayout();

        window = readme.Group!.Float!.Bounds;
        Assert.True(window.X < host.ActualSize.Width && window.Y >= 0);
    }

    [Fact]
    public void ATabOfAWindowOfTwoLeavesItAndTheWindowStays()
    {
        var (host, editor, readme, files, _) = Ide();
        var (form, _) = Show(host);

        host.Float(readme, new Rectangle(300, 120, 320, 220));
        host.DockTo(editor, DockPosition.Tabbed, readme);
        form.UpdateLayout();

        Assert.Equal(DockState.Floating, editor.State);
        Assert.True(readme.Group!.View!.Strip.FindOwner() is not null, "a window of two shows its tabs");

        Point tab = TabOf(editor);

        form.OnPointerDown(tab);
        form.OnPointerMove(new Point(tab.X, tab.Y + 70));
        form.OnPointerMove(BoundsOf(files).Center);

        DockHost.DockTarget middle = host.DragTargets.Single(t => t.Group == files.Group && t.Position == DockPosition.Tabbed);
        Point button = ToForm(host, middle.Button);

        form.OnPointerMove(button);
        form.OnPointerUp(button);

        Assert.Same(files.Group, editor.Group);
        Assert.Equal(DockState.Floating, readme.State);
    }

    [Fact]
    public void ADraggedOutTabLeavesTheStrip()
    {
        var strip = new TabStrip { CanDragOut = true, Items = { new TabStripItem("one"), new TabStripItem("two") } };
        var form = new Form { Size = new Size(400, 200), Content = new StackPanel { Children = { strip } } };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        TabStripItem? left = null;
        strip.TabDraggedOut += (_, e) => left = e.Item;

        Rectangle tab = strip.TabRect(1);
        Point at = strip.GetAbsolutePosition();
        var press = new Point(at.X + tab.Center.X, at.Y + tab.Center.Y);

        form.OnPointerDown(press);
        form.OnPointerMove(new Point(press.X, press.Y + 10));
        Assert.Null(left);

        form.OnPointerMove(new Point(press.X, press.Y + 80));
        form.OnPointerUp(new Point(press.X, press.Y + 80));

        Assert.Same(strip.Items[1], left);
        Assert.Equal(2, strip.Items.Count);
    }

    [Fact]
    public void TheMenuOfATabOffersWhatItsFlagsAllow()
    {
        WithCulture("en-US", () =>
        {
            var (host, editor, readme, _, _) = Ide();
            Show(host);

            readme.CanFloat = false;
            editor.CanClose = false;

            List<MenuItem> menu = host.MenuFor(readme);
            List<string> texts = [.. menu.Where(m => !m.IsSeparator).Select(m => m.Text)];

            Assert.DoesNotContain(Localization.Get(ZfText.DockFloat), texts);
            Assert.Contains(Localization.Get(ZfText.DockAutoHide), texts);
            Assert.Contains(Localization.Get(ZfText.DockClose), texts);
            Assert.DoesNotContain(Localization.Get(ZfText.DockCloseOthers), texts);
            menu.Single(m => m.Text == "Auto hide").RaiseClick();
            Assert.Equal(DockState.AutoHidden, readme.State);
        });
    }

    private static void WithCulture(string name, Action test)
    {
        CultureInfo before = Localization.Culture;

        try
        {
            Localization.Culture = new CultureInfo(name);
            test();
        }
        finally
        {
            Localization.Culture = before;
        }
    }
}
