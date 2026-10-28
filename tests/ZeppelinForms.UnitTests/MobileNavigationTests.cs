using Xunit;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Navigation;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.UnitTests;

/// <summary>0.19: the frame of a phone application — a navigation stack and its routes, the
/// system's back, the app bar, the navigation bar and the scaffold, the bottom sheet, the
/// swipe view, the snackbar, the safe area and the keyboard.</summary>
[Collection("Platform")]
public class MobileNavigationTests
{
    private static readonly TimeSpan Done = TimeSpan.FromMilliseconds(1000);

    private static (Form Form, HeadlessPlatform Platform) Show(UIElement content, string? initialRoute = null)
    {
        var platform = new HeadlessPlatform { InitialRoute = initialRoute };
        var form = new Form { Size = new Size(400, 700), Content = content };

        platform.CreateWindow(form);
        form.UpdateLayout();

        return (form, platform);
    }

    /// <summary>Real time for what the form schedules — a snackbar's timeout, a step after a
    /// layout — pumping the platform until the condition holds.</summary>
    private static void Pump(Form form, HeadlessPlatform platform, Func<bool> until)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);

        while (!until() && DateTime.UtcNow < deadline)
        {
            form.Clock.Advance(TimeSpan.FromMilliseconds(50));
            platform.PumpAll();
            Thread.Sleep(10);
        }
    }

    private sealed class ProbePage(string title) : Page(new Label { Text = title }, title)
    {
        public List<string> Log { get; } = [];

        public bool MayLeave { get; set; } = true;

        protected internal override void OnNavigatedTo(NavigationEventArgs e) => Log.Add($"to:{e.Kind}");

        protected internal override void OnNavigatedFrom(NavigationEventArgs e) => Log.Add($"from:{e.Kind}");

        protected internal override Task<bool> CanLeaveAsync() => Task.FromResult(MayLeave);
    }

    // ===== the stack =====

    [Fact]
    public void PushAndPopMoveThroughTheStack()
    {
        var root = new ProbePage("Root");
        var host = new NavigationHost { RootPage = root };
        var (form, _) = Show(host);

        var details = new ProbePage("Details");
        Task push = host.PushAsync(details);

        // the transition runs on the form's clock
        Assert.False(push.IsCompleted);
        Assert.True(details.IsVisible);
        Assert.True(root.IsVisible);

        form.Clock.Advance(Done);

        Assert.True(push.IsCompleted);
        Assert.Same(details, host.CurrentPage);
        Assert.False(root.IsVisible);
        Assert.True(host.CanGoBack);
        Assert.Same(host, details.NavigationHost);
        Assert.Equal(["from:Push"], root.Log.Skip(1));
        Assert.Equal(["to:Push"], details.Log);

        Task<bool> pop = host.PopAsync();
        form.Clock.Advance(Done);

        Assert.True(pop.Result);
        Assert.Same(root, host.CurrentPage);
        Assert.True(root.IsVisible);
        Assert.DoesNotContain(details, host.Children);
        Assert.Null(details.NavigationHost);
        Assert.False(host.CanGoBack);
    }

    [Fact]
    public void APushMovesThePagesWithTheProgress()
    {
        var root = new Page(new Label(), "Root");
        var host = new NavigationHost { RootPage = root, TransitionDurationMs = 200 };
        var (form, _) = Show(host);

        var next = new Page(new Label(), "Next");
        _ = host.PushAsync(next);

        form.Clock.Advance(TimeSpan.FromMilliseconds(100));

        // the new page comes in from the right; the old one drifts left
        Assert.True(next.Position.X > 0 && next.Position.X < host.ActualSize.Width);
        Assert.True(root.Position.X < 0);

        form.Clock.Advance(Done);

        Assert.Equal(0f, next.Position.X);
    }

    [Fact]
    public void APageThatWontBeLeftStays()
    {
        var host = new NavigationHost { RootPage = new Page(new Label(), "Root") };
        var (form, _) = Show(host);

        var editor = new ProbePage("Editor") { MayLeave = false };
        _ = host.PushAsync(editor, animated: false);

        Task<bool> pop = host.PopAsync();
        form.Clock.Advance(Done);

        Assert.False(pop.Result);
        Assert.Same(editor, host.CurrentPage);
    }

    [Fact]
    public void NavigatingCanBeCancelled()
    {
        var host = new NavigationHost { RootPage = new Page(new Label(), "Root") };
        Show(host);

        host.Navigating += (_, e) => e.Cancel = e.Kind == NavigationKind.Push;

        _ = host.PushAsync(new Page(new Label(), "Never"), animated: false);

        Assert.Single(host.Stack);
    }

    [Fact]
    public void PopToRootDropsThePagesBetween()
    {
        var root = new Page(new Label(), "Root");
        var host = new NavigationHost { RootPage = root };
        var (form, platform) = Show(host);

        _ = host.PushAsync(new Page(new Label(), "A"), animated: false);
        _ = host.PushAsync(new Page(new Label(), "B"), animated: false);
        _ = host.PushAsync(new Page(new Label(), "C"), animated: false);

        Task done = host.PopToRootAsync();
        Pump(form, platform, () => done.IsCompleted);

        Assert.True(done.IsCompleted);
        Assert.Equal([root], host.Stack);
        Assert.Equal([root], host.Children);
    }

    [Fact]
    public void ReplaceKeepsTheDepth()
    {
        var host = new NavigationHost { RootPage = new Page(new Label(), "Root") };
        Show(host);

        _ = host.PushAsync(new Page(new Label(), "Sign in"), animated: false);
        _ = host.ReplaceAsync(new Page(new Label(), "Account"), animated: false);

        Assert.Equal(["Root", "Account"], host.Stack.Select(p => p.Title));
    }

    [Fact]
    public void APushedPageReturnsItsResult()
    {
        var host = new NavigationHost { RootPage = new Page(new Label(), "Root") };
        var (form, platform) = Show(host);

        var picker = new Page(new Label(), "Pick");
        Task<string?> result = host.PushForResultAsync<string>(picker, animated: false);
        platform.PumpAll();

        Assert.False(result.IsCompleted);

        _ = picker.ReturnAsync("blue");
        Pump(form, platform, () => result.IsCompleted);

        Assert.Equal("blue", result.Result);
        Assert.Single(host.Stack);
    }

    [Fact]
    public void APageLeftAnotherWayReturnsTheDefault()
    {
        var host = new NavigationHost { RootPage = new Page(new Label(), "Root") };
        var (form, platform) = Show(host);

        var picker = new Page(new Label(), "Pick");
        Task<string?> result = host.PushForResultAsync<string>(picker, animated: false);

        _ = host.PopAsync();
        Pump(form, platform, () => result.IsCompleted);

        Assert.Null(result.Result);
    }

    // ===== routes =====

    [Fact]
    public void ARouteMakesItsPageWithTheQuery()
    {
        var host = new NavigationHost { RootPage = new Page(new Label(), "Inbox") { Route = "inbox" } };
        var (form, _) = Show(host);

        host.RegisterRoute("message", p => new Page(new Label(), "Message " + p["id"]));

        _ = host.GoToAsync("message?id=42&from=Anna%20B");
        form.Clock.Advance(Done);

        Page page = host.CurrentPage!;

        Assert.Equal("Message 42", page.Title);
        Assert.Equal("message", page.Route);
        Assert.Equal("Anna B", page.Parameters["from"]);
        Assert.Equal("/inbox/message?id=42&from=Anna%20B", host.Location());
    }

    [Fact]
    public void AnAbsoluteLocationStartsFromTheRoot()
    {
        var host = new NavigationHost { RootPage = new Page(new Label(), "Inbox") { Route = "inbox" } };
        var (form, _) = Show(host);

        host.RegisterRoute("folder", () => new Page(new Label(), "Folder"));
        host.RegisterRoute("message", () => new Page(new Label(), "Message"));

        _ = host.GoToAsync("folder/message");
        form.Clock.Advance(Done);
        Assert.Equal(3, host.Stack.Count);

        _ = host.GoToAsync("/inbox/folder");
        form.Clock.Advance(Done);

        Assert.Equal(["Inbox", "Folder"], host.Stack.Select(p => p.Title));
    }

    [Fact]
    public async Task AnUnknownRouteMovesNothing()
    {
        var host = new NavigationHost { RootPage = new Page(new Label(), "Root") };
        Show(host);

        host.RegisterRoute("known", () => new Page(new Label(), "Known"));

        await Assert.ThrowsAsync<ArgumentException>(() => host.GoToAsync("known/unknown"));
        Assert.Single(host.Stack);
    }

    [Fact]
    public void TheHistoryFollowsTheStack()
    {
        var host = new NavigationHost { RootPage = new Page(new Label(), "Inbox") { Route = "inbox" } };
        host.RegisterRoute("message", () => new Page(new Label(), "Message"));

        var (form, _) = Show(host);
        var window = (HeadlessWindow)form.PlatformWindow!;

        // the page's own entry names the root
        Assert.Equal(["/inbox"], window.HistoryEntries);

        _ = host.GoToAsync("message");
        form.Clock.Advance(Done);

        Assert.Equal(["/inbox", "/inbox/message"], window.HistoryEntries);

        _ = host.PopAsync();
        form.Clock.Advance(Done);

        Assert.Equal(["/inbox"], window.HistoryEntries);
    }

    [Fact]
    public void TheBrowsersBackDoesntGoBackInItsHistoryAgain()
    {
        var host = new NavigationHost { RootPage = new Page(new Label(), "Inbox") { Route = "inbox" } };
        host.RegisterRoute("message", () => new Page(new Label(), "Message"));

        var (form, _) = Show(host);
        var window = (HeadlessWindow)form.PlatformWindow!;

        _ = host.GoToAsync("message");
        form.Clock.Advance(Done);

        // the browser went back by itself: its entry is gone already
        window.HistoryEntries.RemoveAt(window.HistoryEntries.Count - 1);

        Assert.True(form.RequestBack(BackSource.Browser));
        form.Clock.Advance(Done);

        Assert.Single(host.Stack);
        Assert.Equal(["/inbox"], window.HistoryEntries);
    }

    [Fact]
    public void TheBrowsersBackGoesToTheStackThatPushedTheEntry()
    {
        var first = new NavigationHost { RootPage = new Page(new Label(), "Inbox") { Route = "inbox" } };
        first.RegisterRoute("message", () => new Page(new Label(), "Message"));

        var second = new NavigationHost { RootPage = new Page(new Label(), "Settings") };

        var both = new StackPanel { Orientation = Orientation.Vertical };
        both.Children.Add(first);
        both.Children.Add(second);

        var (form, _) = Show(both);
        var window = (HeadlessWindow)form.PlatformWindow!;

        // the second stack has no routes: it leaves the address to the first
        Assert.Equal(["/inbox"], window.HistoryEntries);

        _ = first.GoToAsync("message", animated: false);
        form.Clock.Advance(Done);

        _ = second.PushAsync(new Page(new Label(), "Account"), animated: false);
        form.Clock.Advance(Done);

        // the first destination is left for the second, as a scaffold does
        first.IsVisible = false;

        // the browser's back takes the latest entry, the second stack's
        window.HistoryEntries.RemoveAt(window.HistoryEntries.Count - 1);
        Assert.True(form.RequestBack(BackSource.Browser));
        form.Clock.Advance(Done);

        Assert.Single(second.Stack);
        Assert.Equal(2, first.Stack.Count);

        // and the next one the first stack's, though it isn't seen
        window.HistoryEntries.RemoveAt(window.HistoryEntries.Count - 1);
        Assert.True(form.RequestBack(BackSource.Browser));
        form.Clock.Advance(Done);

        Assert.Single(first.Stack);
        Assert.Equal(["/inbox"], window.HistoryEntries);

        // nothing of theirs is left: the browser's back has nothing to take
        Assert.False(form.RequestBack(BackSource.Browser));
    }

    [Fact]
    public void AnAddressWithARouteOpensItsPage()
    {
        var host = new NavigationHost { RootPage = new Page(new Label(), "Inbox") { Route = "inbox" } };
        host.RegisterRoute("message", p => new Page(new Label(), "Message " + p["id"]));

        var (form, _) = Show(host, initialRoute: "/inbox/message?id=7");
        form.Clock.Advance(Done);

        Assert.Equal(["Inbox", "Message 7"], host.Stack.Select(p => p.Title));
    }

    // ===== back =====

    [Fact]
    public void BackGoesToTheInnermostVisibleStack()
    {
        var inner = new NavigationHost { RootPage = new Page(new Label(), "Inner root") };
        var outer = new NavigationHost { RootPage = new Page(inner, "Outer root") };

        var (form, _) = Show(outer);

        _ = outer.PushAsync(new Page(new Label(), "Outer 2"), animated: false);
        _ = outer.PopAsync(animated: false);
        _ = inner.PushAsync(new Page(new Label(), "Inner 2"), animated: false);
        _ = outer.PushAsync(new Page(new Label(), "Outer 2"), animated: false);

        // the inner stack is under the outer one's second page: hidden, it is skipped
        Assert.True(form.RequestBack(BackSource.System));
        form.Clock.Advance(Done);

        Assert.Single(outer.Stack);
        Assert.Equal(2, inner.Stack.Count);

        // now it shows, and goes first
        Assert.True(form.RequestBack(BackSource.System));
        form.Clock.Advance(Done);

        Assert.Single(inner.Stack);
    }

    [Fact]
    public void BackNobodyTookIsTheApplications()
    {
        var host = new NavigationHost { RootPage = new Page(new Label(), "Root") };
        var (form, _) = Show(host);

        BackSource? heard = null;
        form.BackRequested += (_, e) => heard = e.Source;

        Assert.False(form.RequestBack(BackSource.System));
        Assert.Equal(BackSource.System, heard);
    }

    [Fact]
    public void BackClosesAFlyoutFirst()
    {
        var host = new NavigationHost { RootPage = new Page(new Label(), "Root") };
        var (form, _) = Show(host);

        _ = host.PushAsync(new Page(new Label(), "Two"), animated: false);

        form.ShowContextMenu([new MenuItem { Text = "Copy" }], new Point(10, 10));
        Assert.Contains(form.Overlays, o => o is MenuList);

        Assert.True(form.RequestBack(BackSource.System));

        Assert.DoesNotContain(form.Overlays, o => o is MenuList);
        Assert.Equal(2, host.Stack.Count);
    }

    [Fact]
    public void AltLeftGoesBack()
    {
        var host = new NavigationHost { RootPage = new Page(new Label(), "Root") };
        var (form, _) = Show(host);

        _ = host.PushAsync(new Page(new Label(), "Two"), animated: false);

        HeadlessInput.PressKey(form, Key.Left, KeyModifiers.Alt);
        form.Clock.Advance(Done);

        Assert.Single(host.Stack);
    }

    [Fact]
    public void ASwipeFromTheEdgeGoesBack()
    {
        var host = new NavigationHost { RootPage = new Page(new Label(), "Root") };
        var (form, _) = Show(host);

        _ = host.PushAsync(new Page(new Label(), "Two"), animated: false);
        form.UpdateLayout();

        HeadlessInput.Swipe(form, new Point(4, 300), new Point(300, 300));
        form.Clock.Advance(Done);

        Assert.Single(host.Stack);
    }

    [Fact]
    public void ASwipeFromTheMiddleDoesnt()
    {
        var host = new NavigationHost { RootPage = new Page(new Label(), "Root") };
        var (form, _) = Show(host);

        _ = host.PushAsync(new Page(new Label(), "Two"), animated: false);
        form.UpdateLayout();

        HeadlessInput.Swipe(form, new Point(150, 300), new Point(390, 300));
        form.Clock.Advance(Done);

        Assert.Equal(2, host.Stack.Count);
    }

    // ===== the app bar =====

    [Fact]
    public void TheAppBarFollowsTheStack()
    {
        var host = new NavigationHost { RootPage = new Page(new Label(), "Inbox") };
        var bar = new AppBar { Host = host };
        var (form, _) = Show(new StackPanel { Children = { bar, host } });

        Assert.Equal("Inbox", bar.ShownTitle);
        Assert.DoesNotContain(bar.Children, c => c is AppBarButton { IsVisible: true });

        var message = new Page(new Label(), "Message");
        message.Actions.Add(new AppBarButton(IconSource.FromPath("M0 0h1v1z"), "Star"));

        _ = host.PushAsync(message, animated: false);
        form.UpdateLayout();

        Assert.Equal("Message", bar.ShownTitle);
        Assert.Contains(bar.Children, c => c is AppBarButton { Text: "Star" });

        // the back button: the first visible one
        var back = bar.Children.OfType<AppBarButton>().First(b => b.IsVisible);
        Assert.Equal("Back", back.Text);

        back.PerformAccessibilityActivation();
        form.Clock.Advance(Done);

        Assert.Equal("Inbox", bar.ShownTitle);
        Assert.DoesNotContain(bar.Children, c => c is AppBarButton { Text: "Star" });
    }

    [Fact]
    public void TheAppBarKeepsBelowTheStatusBar()
    {
        var bar = new AppBar { Title = "Title" };
        var (form, _) = Show(new StackPanel { Children = { bar } });

        float plain = bar.ActualSize.Height;

        form.SetSafeAreaInsets(new Thickness(0, 24, 0, 16));
        form.UpdateLayout();

        Assert.Equal(plain + 24f, bar.ActualSize.Height);
    }

    // ===== the scaffold =====

    [Fact]
    public void TheNavigationBarSwitchesTheBodyAndKeepsIt()
    {
        var made = 0;
        var inbox = new NavigationHost { RootPage = new Page(new Label(), "Inbox") };

        var scaffold = new Scaffold
        {
            AppBar = new AppBar(),
            NavigationBar = new NavigationBar
            {
                Items =
                {
                    new NavigationBarItem("Inbox") { Content = inbox },
                    new NavigationBarItem("Settings", content: () =>
                    {
                        made++;
                        return new NavigationHost { RootPage = new Page(new Label(), "Settings") };
                    }),
                },
            },
        };

        var (form, _) = Show(scaffold);

        Assert.Same(inbox, scaffold.ShownBody);
        Assert.Equal("Inbox", scaffold.AppBar!.ShownTitle);

        scaffold.NavigationBar!.SelectedIndex = 1;
        scaffold.NavigationBar.SelectedIndex = 0;
        scaffold.NavigationBar.SelectedIndex = 1;

        Assert.Equal(1, made);
        Assert.Equal("Settings", scaffold.AppBar.ShownTitle);
        Assert.False(inbox.IsVisible);

        // the body lies between the bars
        form.UpdateLayout();
        Rectangle body = new(scaffold.ShownBody!.Position, scaffold.ShownBody.ActualSize);

        Assert.Equal(scaffold.AppBar.ActualSize.Height, body.Y);
        Assert.Equal(scaffold.NavigationBar.Position.Y, body.Bottom);
    }

    [Fact]
    public void ChoosingTheShownDestinationAgainGoesToItsRoot()
    {
        var inbox = new NavigationHost { RootPage = new Page(new Label(), "Inbox") };
        var bar = new NavigationBar { Items = { new NavigationBarItem("Inbox") { Content = inbox }, new NavigationBarItem("Other") } };
        var (form, _) = Show(new Scaffold { NavigationBar = bar });

        _ = inbox.PushAsync(new Page(new Label(), "Two"), animated: false);
        form.UpdateLayout();

        bar.Children[0].PerformAccessibilityActivation();
        form.Clock.Advance(Done);

        Assert.Single(inbox.Stack);
    }

    // ===== the bottom sheet =====

    [Fact]
    public void ABottomSheetClosesOnBackAndReportsIt()
    {
        var (form, _) = Show(new Label { Text = "Page" });

        var sheet = new BottomSheet(new Label { Text = "Options" });
        Task closed = sheet.ShowAsync(form);
        form.Clock.Advance(Done);

        Assert.True(sheet.IsOpen);
        Assert.Contains(form.Overlays, o => o.Children().Contains(sheet));

        Assert.True(form.RequestBack(BackSource.System));
        form.Clock.Advance(Done);

        Assert.True(closed.IsCompleted);
        Assert.False(sheet.IsOpen);
        Assert.DoesNotContain(form.Overlays, o => o.Children().Contains(sheet));
    }

    [Fact]
    public void ABottomSheetClosesWithEscape()
    {
        var (form, _) = Show(new Label { Text = "Page" });

        var sheet = new BottomSheet(new Label { Text = "Options" });
        sheet.Show(form);
        form.Clock.Advance(Done);

        HeadlessInput.PressKey(form, Key.Escape);
        form.Clock.Advance(Done);

        Assert.False(sheet.IsOpen);
    }

    [Fact]
    public void ASheetThatIsntDismissibleStays()
    {
        var (form, _) = Show(new Label { Text = "Page" });

        var sheet = new BottomSheet(new Label { Text = "Required" }) { IsDismissible = false };
        sheet.Show(form);
        form.Clock.Advance(Done);

        Assert.False(form.RequestBack(BackSource.System));
        Assert.True(sheet.IsOpen);

        sheet.Close();
        form.Clock.Advance(Done);

        Assert.False(sheet.IsOpen);
    }

    [Fact]
    public void ABottomSheetStandsAtTheBottom()
    {
        var (form, _) = Show(new Label { Text = "Page" });

        var sheet = new BottomSheet(new Label { Text = "Options", Size = new Size(200, 100) });
        sheet.Show(form);
        form.Clock.Advance(Done);
        form.UpdateLayout();

        Point at = sheet.GetAbsolutePosition();

        Assert.Equal(form.ClientSize.Height, at.Y + sheet.ActualSize.Height, 1f);
        Assert.Equal(form.ClientSize.Width, sheet.ActualSize.Width, 1f);
    }

    // ===== the swipe view =====

    [Fact]
    public void ASwipeViewRevealsAndRunsItsActions()
    {
        var invoked = new List<string>();

        var swipe = new SwipeView(new Label { Text = "Row", Size = new Size(float.NaN, 48) }) { HorizontalAlignment = HorizontalAlignment.Stretch };
        swipe.RightItems.Add(new SwipeItem("Delete", invoked: (_, _) => invoked.Add("Delete")));
        swipe.LeftItems.Add(new SwipeItem("Star", invoked: (_, _) => invoked.Add("Star")));

        var (form, _) = Show(new StackPanel { CrossAxisAlignment = CrossAxisAlignment.Stretch, Children = { swipe } });

        float closed = swipe.Child!.Position.X;

        swipe.Open(Input.Gestures.SwipeDirection.Left);
        form.Clock.Advance(Done);
        form.UpdateLayout();

        Assert.True(swipe.IsOpen);
        Assert.Equal(-80f, swipe.Child.Position.X - closed, 1f);

        // a tap on the uncovered action runs it and covers it again
        Point at = swipe.GetAbsolutePosition();
        HeadlessInput.Click(form, at.X + swipe.ActualSize.Width - 20, at.Y + 20);
        form.Clock.Advance(Done);

        Assert.Equal(["Delete"], invoked);
        Assert.False(swipe.IsOpen);

        // its actions are in its context menu too
        List<MenuItem>? menu = swipe.ContextMenu ?? DefaultMenu(swipe);
        Assert.Equal(["Star", "Delete"], menu!.Select(i => i.Text));
    }

    private static List<MenuItem>? DefaultMenu(UIElement element) => element.DefaultContextMenu();

    [Fact]
    public void ASwipeAllTheWayExecutes()
    {
        var invoked = 0;

        var swipe = new SwipeView(new Label { Text = "Row", Size = new Size(float.NaN, 48) }) { RightMode = SwipeMode.Execute, HorizontalAlignment = HorizontalAlignment.Stretch };
        swipe.RightItems.Add(new SwipeItem("Delete", invoked: (_, _) => invoked++));

        var (form, _) = Show(new StackPanel { CrossAxisAlignment = CrossAxisAlignment.Stretch, Children = { swipe } });
        Point at = swipe.GetAbsolutePosition();

        HeadlessInput.Swipe(form, new Point(at.X + 380, at.Y + 20), new Point(at.X + 20, at.Y + 20));
        form.Clock.Advance(Done);

        Assert.Equal(1, invoked);
        Assert.False(swipe.IsOpen);
    }

    // ===== the snackbar =====

    [Fact]
    public void ASnackbarTimesOut()
    {
        var (form, platform) = Show(new Label { Text = "Page" });

        Task<bool> shown = form.ShowSnackbarAsync("Saved", duration: TimeSpan.FromMilliseconds(300));
        form.Clock.Advance(TimeSpan.FromMilliseconds(500));

        Assert.Contains(form.Overlays, o => o is Snackbar);

        // the timeout is the form's schedule: real time
        Pump(form, platform, () => shown.IsCompleted);

        Assert.False(shown.Result);
        Assert.DoesNotContain(form.Overlays, o => o is Snackbar);
    }

    [Fact]
    public void ASnackbarsActionIsReported()
    {
        var (form, platform) = Show(new Label { Text = "Page" });

        Task<bool> shown = form.ShowSnackbarAsync("Deleted", "Undo");
        form.Clock.Advance(TimeSpan.FromMilliseconds(500));
        form.UpdateLayout();

        var bar = form.Overlays.OfType<Snackbar>().Single();
        bar.Children.OfType<Button>().Single().PerformAccessibilityActivation();
        Pump(form, platform, () => shown.IsCompleted);

        Assert.True(shown.Result);
    }

    [Fact]
    public void SnackbarsComeOneAtATime()
    {
        var (form, platform) = Show(new Label { Text = "Page" });

        Task<bool> first = form.ShowSnackbarAsync("First", duration: TimeSpan.FromMilliseconds(300));
        Task<bool> second = form.ShowSnackbarAsync("Second", duration: TimeSpan.FromMilliseconds(300));
        form.Clock.Advance(TimeSpan.FromMilliseconds(300));

        Assert.Single(form.Overlays.OfType<Snackbar>());
        Assert.Equal("First", form.Overlays.OfType<Snackbar>().Single().Message);

        Pump(form, platform, () => first.IsCompleted);
        Assert.False(second.IsCompleted);

        Pump(form, platform, () => second.IsCompleted);
        Assert.True(second.IsCompleted);
    }

    // ===== the safe area and the keyboard =====

    [Fact]
    public void ASafeAreaKeepsItsContentOutOfTheInsets()
    {
        var content = new Border { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        var safe = new SafeArea(content) { Edges = SafeAreaEdges.Top | SafeAreaEdges.Bottom };
        var (form, _) = Show(safe);

        form.SetSafeAreaInsets(new Thickness(10, 30, 10, 20));
        form.UpdateLayout();

        Assert.Equal(30f, content.Position.Y - safe.Padding.Top, 1f);
        Assert.Equal(0f, content.Position.X - safe.Padding.Left, 1f);
        Assert.Equal(form.ClientSize.Height - 50f - safe.Padding.Vertical, content.ActualSize.Height, 1f);
    }

    [Fact]
    public void AnElementIsScrolledIntoView()
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, OverflowY = Overflow.Auto, Size = new Size(200, 200) };

        for (int i = 0; i < 30; i++)
            panel.Children.Add(new Label { Text = "Row " + i, Size = new Size(float.NaN, 30) });

        var (form, _) = Show(panel);

        UIElement last = panel.Children[^1];
        last.BringIntoView();
        form.UpdateLayout();

        Assert.True(panel.ScrollY > 0);
        Assert.True(last.Position.Y + last.ActualSize.Height <= 200f + 0.5f);
        Assert.True(last.Position.Y >= 0f);
    }

    [Fact]
    public void TheKeyboardBringsTheFieldTypedInBackIntoView()
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, OverflowY = Overflow.Auto };

        for (int i = 0; i < 20; i++)
            panel.Children.Add(new Label { Text = "Row " + i, Size = new Size(float.NaN, 30) });

        var field = new TextBox();
        panel.Children.Add(field);

        var (form, platform) = Show(panel);

        form.FocusElement(field);
        Assert.Same(field, form.FocusedElement);

        // the keyboard came up: the form got shorter, and the field was at its bottom
        form.ClientSize = new Size(400, 300);
        Pump(form, platform, () => panel.ScrollY > 0);
        form.UpdateLayout();

        Point at = field.GetAbsolutePosition();
        Assert.True(at.Y + field.ActualSize.Height <= 300f + 0.5f);
    }
}

internal static class OverlayExtensions
{
    /// <summary>The children of an overlay that is a panel; none of another.</summary>
    public static IEnumerable<UIElement> Children(this UIElement overlay) =>
        overlay is PanelControl panel ? panel.Children : [];
}
