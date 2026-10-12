using Xunit;
using ZeppelinForms.Accessibility;
using ZeppelinForms.Core.Globalization;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.UnitTests;

/// <summary>A split view's menu: entries in <see cref="SplitView.Items"/> become a
/// pane the view draws itself, and choosing one shows its content.</summary>
[Collection("Platform")]
public class SplitViewNavigationTests
{
    private const float Width = 800;
    private const float Height = 600;

    private static (Form Form, SplitView View) Create(SplitViewDisplayMode mode = SplitViewDisplayMode.CompactInline)
    {
        var view = new SplitView { DisplayMode = mode };
        var form = new Form { Size = new Size(Width, Height), Content = view };

        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return (form, view);
    }

    private static void Settle(Form form)
    {
        form.Clock.Advance(TimeSpan.FromSeconds(1));
        form.UpdateLayout();
    }

    /// <summary>The middle of an entry's icon: inside the compact strip, so it is
    /// hit whether the pane is open or not.</summary>
    private static Point At(SplitView view, SplitViewItem item)
    {
        SplitViewMenu menu = view.Menu!;
        SplitViewMenu.MenuRow row = menu.Rows.Single(r => ReferenceEquals(r.Item, item));
        Point origin = menu.GetAbsolutePosition();

        return new Point(origin.X + view.CompactPaneLength / 2f, origin.Y + row.Bounds.Y + row.Bounds.Height / 2f);
    }

    private static Point AtButton(SplitView view, SplitViewMenu.RowKind kind)
    {
        SplitViewMenu menu = view.Menu!;
        SplitViewMenu.MenuRow row = menu.Rows.Single(r => r.Kind == kind);
        Point origin = menu.GetAbsolutePosition();

        return new Point(origin.X + view.CompactPaneLength / 2f, origin.Y + row.Bounds.Y + row.Bounds.Height / 2f);
    }

    private static void Click(Form form, Point at)
    {
        form.OnPointerDown(at);
        form.OnPointerUp(at);
        form.UpdateLayout();
    }

    private static (SplitViewItem Item, Func<int> Made) Page(string text)
    {
        int made = 0;
        var item = new SplitViewItem(text, content: () =>
        {
            made++;
            return new Label { Text = text };
        });

        return (item, () => made);
    }

    // ===== content =====

    [Fact]
    public void TheFirstEntryIsSelectedAndShown()
    {
        var (form, view) = Create();
        var (home, made) = Page("Home");
        var (charts, _) = Page("Charts");

        view.Items.Add(home);
        view.Items.Add(charts);
        form.UpdateLayout();

        Assert.True(view.HasMenu);
        Assert.Same(home, view.SelectedItem);
        Assert.Equal("Home", Assert.IsType<Label>(view.Content).Text);
        Assert.Equal(1, made());
    }

    [Fact]
    public void ClickingAnEntryShowsItsContent()
    {
        var (form, view) = Create();
        var (home, _) = Page("Home");
        var (charts, _) = Page("Charts");
        view.Items.Add(home);
        view.Items.Add(charts);
        form.UpdateLayout();

        List<string> events = [];
        view.ItemInvoked += (_, e) => events.Add("invoked " + e.Item.Text);
        view.SelectionChanged += (_, e) => events.Add($"selected {e.OldItem?.Text} -> {e.NewItem?.Text}");

        Click(form, At(view, charts));

        Assert.Same(charts, view.SelectedItem);
        Assert.Equal("Charts", Assert.IsType<Label>(view.Content).Text);

        // invoked first, as in WinUI: the handler may still change its mind
        Assert.Equal(["invoked Charts", "selected Home -> Charts"], events);
    }

    [Fact]
    public void ContentIsMadeOnceAndKept()
    {
        var (form, view) = Create();
        var (home, homeMade) = Page("Home");
        var (charts, chartsMade) = Page("Charts");
        view.Items.Add(home);
        view.Items.Add(charts);
        form.UpdateLayout();

        UIElement first = view.Content!;

        Click(form, At(view, charts));
        Click(form, At(view, home));

        // coming back finds the page as it was left
        Assert.Same(first, view.Content);
        Assert.Equal(1, homeMade());
        Assert.Equal(1, chartsMade());
    }

    [Fact]
    public void AnEntryWithoutContentLeavesTheContentToTheApplication()
    {
        var (form, view) = Create();
        var (home, _) = Page("Home");
        var plain = new SplitViewItem("Plain");
        view.Items.Add(home);
        view.Items.Add(plain);
        form.UpdateLayout();

        var mine = new Label { Text = "the application's" };
        view.SelectionChanged += (_, e) => { if (ReferenceEquals(e.NewItem, plain)) view.Content = mine; };

        Click(form, At(view, plain));

        Assert.Same(plain, view.SelectedItem);
        Assert.Same(mine, view.Content);
    }

    [Fact]
    public void AnActionEntryIsInvokedButNotSelected()
    {
        var (form, view) = Create();
        var (home, _) = Page("Home");
        var signOut = new SplitViewItem("Sign out") { SelectsOnInvoke = false };
        view.Items.Add(home);
        view.FooterItems.Add(signOut);
        form.UpdateLayout();

        SplitViewItem? invoked = null;
        view.ItemInvoked += (_, e) => invoked = e.Item;

        Click(form, At(view, signOut));

        Assert.Same(signOut, invoked);
        Assert.Same(home, view.SelectedItem);
        Assert.Equal("Home", Assert.IsType<Label>(view.Content).Text);
    }

    [Fact]
    public void ADisabledEntryIgnoresClicks()
    {
        var (form, view) = Create();
        var (home, _) = Page("Home");
        var (off, _) = Page("Off");
        off.IsEnabled = false;
        view.Items.Add(home);
        view.Items.Add(off);
        form.UpdateLayout();

        int invoked = 0;
        view.ItemInvoked += (_, _) => invoked++;

        Click(form, At(view, off));

        Assert.Same(home, view.SelectedItem);
        Assert.Equal(0, invoked);
    }

    [Fact]
    public void HeadersAndSeparatorsAreNotChosen()
    {
        var (form, view) = Create();
        SplitViewItem header = SplitViewItem.Header("Data");
        var (home, _) = Page("Home");

        view.Items.Add(header);
        view.Items.Add(SplitViewItem.Separator());
        view.Items.Add(home);
        form.UpdateLayout();

        // the first entry that can be chosen, past the header
        Assert.Same(home, view.SelectedItem);

        view.SelectedItem = header;
        Assert.Null(view.SelectedItem);
    }

    // ===== the pane =====

    [Fact]
    public void TheToggleButtonOpensAndClosesThePane()
    {
        var (form, view) = Create();
        view.Items.Add(Page("Home").Item);
        form.UpdateLayout();

        Click(form, AtButton(view, SplitViewMenu.RowKind.Toggle));
        Settle(form);
        Assert.True(view.IsPaneOpen);

        Click(form, AtButton(view, SplitViewMenu.RowKind.Toggle));
        Settle(form);
        Assert.False(view.IsPaneOpen);
    }

    [Theory]
    [InlineData(SplitViewDisplayMode.Overlay)]
    [InlineData(SplitViewDisplayMode.Inline)]
    public void APaneThatHidesWholeIsOpenedByAButtonOverTheContent(SplitViewDisplayMode mode)
    {
        // the default mode, Overlay, hid the menu and its button together:
        // nothing on screen opened it
        var (form, view) = Create(mode);
        view.Items.Add(Page("Home").Item);
        form.UpdateLayout();

        Assert.False(view.Menu!.IsEffectivelyVisible);

        // the content's corner, where the open menu has its own button
        Click(form, new Point(view.CompactPaneLength / 2f, view.ItemHeight / 2f));
        Settle(form);

        Assert.True(view.IsPaneOpen);
    }

    [Theory]
    [InlineData(SplitViewDisplayMode.Overlay, false)]
    [InlineData(SplitViewDisplayMode.Inline, false)]
    [InlineData(SplitViewDisplayMode.Overlay, true)]
    public void TheButtonHasABandOfItsOwnAboveTheContent(SplitViewDisplayMode mode, bool open)
    {
        // over the content's corner it covered what was there — in the process
        // monitor the grid's first header, the hover fill coming and going over it
        var (form, view) = Create(mode);
        var page = new StackPanel();
        view.Items.Add(new SplitViewItem("Processes", content: () => page));
        view.IsPaneOpen = open;
        Settle(form);

        Assert.Equal(view.ItemHeight, page.Position.Y);
        Assert.Equal(Height - view.ItemHeight, page.ActualSize.Height);

        // and the band stays while the pane is open: the content doesn't jump
        view.IsPaneOpen = !open;
        Settle(form);

        Assert.Equal(view.ItemHeight, page.Position.Y);
    }

    [Theory]
    [InlineData(SplitViewDisplayMode.CompactInline)]
    [InlineData(SplitViewDisplayMode.CompactOverlay)]
    public void ACompactPaneLeavesTheContentWholeHeight(SplitViewDisplayMode mode)
    {
        var (form, view) = Create(mode);
        var page = new StackPanel();
        view.Items.Add(new SplitViewItem("Processes", content: () => page));
        Settle(form);

        Assert.Equal(0f, page.Position.Y);
        Assert.Equal(Height, page.ActualSize.Height);
    }

    [Fact]
    public void WithoutTheButtonThereIsNoBand()
    {
        var (form, view) = Create(SplitViewDisplayMode.Overlay);
        var page = new StackPanel();
        view.Items.Add(new SplitViewItem("Processes", content: () => page));
        view.IsPaneToggleButtonVisible = false;
        Settle(form);

        Assert.Equal(0f, page.Position.Y);
    }

    [Fact]
    public void ACompactPaneNeedsNoButtonOverTheContent()
    {
        var (form, view) = Create(SplitViewDisplayMode.CompactInline);
        view.Items.Add(Page("Home").Item);
        form.UpdateLayout();

        AccessibilityPeer peer = view.GetAccessibilityPeer()!;

        // the pane and the content, nothing else
        Assert.Equal(2, peer.Children.Count);
    }

    [Fact]
    public void TheButtonOverTheContentGoesWithTheMenu()
    {
        var (form, view) = Create(SplitViewDisplayMode.Overlay);
        view.Pane = new StackPanel();
        view.Content = new Label { Text = "content" };
        form.UpdateLayout();

        // a pane of one's own opens however the application likes: no button of ours
        Click(form, new Point(view.CompactPaneLength / 2f, view.ItemHeight / 2f));
        Settle(form);

        Assert.False(view.IsPaneOpen);
    }

    [Theory]
    [InlineData(SplitViewDisplayMode.CompactInline, SplitViewPanePlacement.Start, false)]
    [InlineData(SplitViewDisplayMode.CompactOverlay, SplitViewPanePlacement.End, false)]
    [InlineData(SplitViewDisplayMode.Overlay, SplitViewPanePlacement.Start, true)]
    public void TheMenuDrawsInEveryState(SplitViewDisplayMode mode, SplitViewPanePlacement placement, bool open)
    {
        var (form, view) = Create(mode);
        view.PanePlacement = placement;
        view.IsBackButtonVisible = true;
        view.PaneTitle = "Monitor";

        var (home, _) = Page("Home");
        home.Badge = "12";
        view.Items.Add(SplitViewItem.Header("Data"));
        view.Items.Add(home);
        view.Items.Add(SplitViewItem.Separator());
        view.Items.Add(new SplitViewItem("Off") { IsEnabled = false });
        view.FooterItems.Add(Page("Settings").Item);

        view.IsPaneOpen = open;
        Settle(form);

        ElementTreeRenderer.Draw(view, new HeadlessGraphics());
    }

    [Fact]
    public void AnOverlayPaneClosesWhenAnEntryIsChosen()
    {
        var (form, view) = Create(SplitViewDisplayMode.CompactOverlay);
        var (home, _) = Page("Home");
        var (charts, _) = Page("Charts");
        view.Items.Add(home);
        view.Items.Add(charts);
        view.IsPaneOpen = true;
        Settle(form);

        Click(form, At(view, charts));
        Settle(form);

        Assert.Same(charts, view.SelectedItem);
        Assert.False(view.IsPaneOpen);
    }

    [Fact]
    public void AnInlinePaneStaysOpen()
    {
        var (form, view) = Create(SplitViewDisplayMode.CompactInline);
        var (home, _) = Page("Home");
        var (charts, _) = Page("Charts");
        view.Items.Add(home);
        view.Items.Add(charts);
        view.IsPaneOpen = true;
        Settle(form);

        Click(form, At(view, charts));
        Settle(form);

        Assert.True(view.IsPaneOpen);
    }

    [Fact]
    public void FooterEntriesStayAtTheBottom()
    {
        var (form, view) = Create();
        view.Items.Add(Page("Home").Item);
        var (settings, _) = Page("Settings");
        view.FooterItems.Add(settings);
        form.UpdateLayout();

        SplitViewMenu.MenuRow row = view.Menu!.Rows.Single(r => ReferenceEquals(r.Item, settings));

        Assert.True(row.InFooter);
        Assert.Equal(Height, row.Bounds.Y + row.Bounds.Height, 0.5f);

        Click(form, At(view, settings));
        Assert.Same(settings, view.SelectedItem);
    }

    [Fact]
    public void ManyEntriesScrollWithTheWheel()
    {
        var (form, view) = Create();

        for (int i = 0; i < 40; i++)
            view.Items.Add(Page("Item " + i).Item);

        form.UpdateLayout();

        Assert.Same(view.Items[0], view.Menu!.Rows.First(r => r.Item is not null).Item);

        // a notch down: the first entry goes up out of the area, the second leads
        form.OnMouseWheel(At(view, view.Items[3]), -120);
        form.UpdateLayout();

        Assert.Same(view.Items[1], view.Menu.Rows.First(r => r.Item is not null).Item);
    }

    // ===== going back =====

    [Fact]
    public void GoBackReturnsToThePreviousEntry()
    {
        var (form, view) = Create();
        var (home, _) = Page("Home");
        var (charts, _) = Page("Charts");
        var (maps, _) = Page("Maps");
        view.Items.Add(home);
        view.Items.Add(charts);
        view.Items.Add(maps);
        form.UpdateLayout();

        Assert.False(view.CanGoBack);

        Click(form, At(view, charts));
        Click(form, At(view, maps));

        Assert.True(view.GoBack());
        Assert.Same(charts, view.SelectedItem);
        Assert.Equal("Charts", Assert.IsType<Label>(view.Content).Text);

        Assert.True(view.GoBack());
        Assert.Same(home, view.SelectedItem);
        Assert.False(view.GoBack());
    }

    [Fact]
    public void TheBackButtonGoesBack()
    {
        var (form, view) = Create();
        view.IsBackButtonVisible = true;
        var (home, _) = Page("Home");
        var (charts, _) = Page("Charts");
        view.Items.Add(home);
        view.Items.Add(charts);
        form.UpdateLayout();

        // nowhere to go yet: the button does nothing
        Click(form, AtButton(view, SplitViewMenu.RowKind.Back));
        Assert.Same(home, view.SelectedItem);

        Click(form, At(view, charts));
        Click(form, AtButton(view, SplitViewMenu.RowKind.Back));

        Assert.Same(home, view.SelectedItem);
    }

    // ===== the keyboard =====

    [Fact]
    public void ArrowsAndEnterChooseAnEntry()
    {
        var (form, view) = Create();
        var (home, _) = Page("Home");
        var (charts, _) = Page("Charts");
        view.Items.Add(home);
        view.Items.Add(charts);
        form.UpdateLayout();

        form.FocusElement(view.Menu!);

        // the keyboard starts on the selected entry
        form.OnKeyDown(Key.Down, KeyModifiers.None, isRepeat: false);
        form.OnKeyDown(Key.Enter, KeyModifiers.None, isRepeat: false);

        Assert.Same(charts, view.SelectedItem);

        // up past the entries to the pane button, and Space presses it
        form.OnKeyDown(Key.Home, KeyModifiers.None, isRepeat: false);
        form.OnKeyDown(Key.Space, KeyModifiers.None, isRepeat: false);
        Settle(form);

        Assert.True(view.IsPaneOpen);
    }

    // ===== tooltips =====

    [Fact]
    public void AnEntryOfAClosedPaneShowsItsTextAsATooltip()
    {
        var (form, view) = Create();
        var (home, _) = Page("Home");
        view.Items.Add(home);
        form.UpdateLayout();

        Assert.Equal("Home", view.Menu!.GetToolTip(At(view, home)));
        Assert.Equal(Localization.Get(ZfText.OpenNavigation), view.Menu.GetToolTip(AtButton(view, SplitViewMenu.RowKind.Toggle)));

        view.IsPaneOpen = true;
        Settle(form);

        // the text is there to read: no tooltip repeating it
        Assert.Null(view.Menu.GetToolTip(At(view, home)));
        Assert.Equal(Localization.Get(ZfText.CloseNavigation), view.Menu.GetToolTip(AtButton(view, SplitViewMenu.RowKind.Toggle)));
    }

    // ===== the entries come and go =====

    [Fact]
    public void RemovingTheSelectedEntryClearsTheSelectionAndTheContent()
    {
        var (form, view) = Create();
        var (home, _) = Page("Home");
        var (charts, _) = Page("Charts");
        view.Items.Add(home);
        view.Items.Add(charts);
        form.UpdateLayout();

        view.Items.Remove(home);

        Assert.Null(view.SelectedItem);
        Assert.Null(view.Content);
        Assert.Null(home.Owner);
    }

    [Fact]
    public void ClearingTheEntriesRemovesTheMenu()
    {
        var (form, view) = Create();
        view.Items.Add(Page("Home").Item);
        view.FooterItems.Add(Page("Settings").Item);
        form.UpdateLayout();

        view.Items.Clear();
        view.FooterItems.Clear();

        Assert.False(view.HasMenu);
        Assert.Null(view.SelectedItem);
        Assert.False(view.CanGoBack);

        // and a pane of one's own is welcome again
        view.Pane = new StackPanel();
        Assert.NotNull(view.Pane);
    }

    [Fact]
    public void AMenuAndAPaneOfOnesOwnDontMix()
    {
        var (_, view) = Create();
        view.Items.Add(Page("Home").Item);

        Assert.Throws<InvalidOperationException>(() => view.Pane = new StackPanel());

        var (_, other) = Create();
        other.Pane = new StackPanel();

        Assert.Throws<InvalidOperationException>(() => other.Items.Add(Page("Home").Item));
    }

    // ===== accessibility =====

    [Fact]
    public void TheMenuIsAListOfItsEntries()
    {
        var (form, view) = Create();
        var (home, _) = Page("Home");
        var (charts, _) = Page("Charts");
        home.Badge = "3";
        view.Items.Add(SplitViewItem.Header("Data"));
        view.Items.Add(home);
        view.Items.Add(SplitViewItem.Separator());
        view.Items.Add(charts);
        form.UpdateLayout();

        AccessibilityPeer menu = view.Menu!.GetAccessibilityPeer()!;

        Assert.Equal(AccessibilityRole.List, menu.Role);

        // the pane button, the header, two entries — the separator says nothing
        Assert.Equal(
            [AccessibilityRole.Button, AccessibilityRole.Heading, AccessibilityRole.ListItem, AccessibilityRole.ListItem],
            menu.Children.Select(c => c.Role).ToList());

        AccessibilityPeer homePeer = menu.Children[2];
        Assert.Equal("Home", homePeer.Name);
        Assert.Equal("3", homePeer.Description);
        Assert.True(homePeer.States.HasFlag(AccessibilityStates.Selected));
        Assert.Equal(new SetPosition(1, 2), homePeer.Position);

        Assert.True(menu.Children[3].Select());
        Assert.Same(charts, view.SelectedItem);

        // the pane button expands the pane, as the split view itself does
        Assert.True(menu.Children[0].Expand());
        Assert.True(view.IsPaneOpen);
    }
}