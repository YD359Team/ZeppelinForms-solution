using Xunit;
using ZeppelinForms.Accessibility;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.UnitTests;

[Collection("Platform")]
public class RefreshContainerTests
{
    private sealed class Row : UnitControl
    {
        public override void Draw(Graphics g) { }

        protected override Size MeasureOverride(Size availableSize) => new(100, 40);
    }

    private static (Form Form, RefreshContainer Container, StackPanel List, HeadlessPlatform Platform) Create(int rows = 50)
    {
        var list = new StackPanel
        {
            Orientation = Orientation.Vertical,
            OverflowY = Overflow.Auto,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        for (int i = 0; i < rows; i++)
            list.Children.Add(new Row());

        var container = new RefreshContainer(list);

        var platform = new HeadlessPlatform();
        var form = new Form { Size = new Size(400, 300), Content = container };

        platform.CreateWindow(form);
        form.UpdateLayout();

        return (form, container, list, platform);
    }

    /// <summary>A slow finger: it stops before it lets go, so nothing is flung.</summary>
    private static void Drag(Form form, Point from, Point to, bool release = true, int steps = 10)
    {
        HeadlessInput.TouchDown(form, 0, from.X, from.Y, 0);

        for (int i = 1; i <= steps; i++)
        {
            float t = (float)i / steps;

            HeadlessInput.TouchMove(
                form, 0,
                from.X + ((to.X - from.X) * t),
                from.Y + ((to.Y - from.Y) * t),
                i * 16);
        }

        if (release)
            HeadlessInput.TouchUp(form, 0, to.X, to.Y, steps * 16 + 200);
    }

    private static void MouseDrag(Form form, Point from, Point to, int steps = 10)
    {
        form.OnPointerDown(from);

        for (int i = 1; i <= steps; i++)
        {
            float t = (float)i / steps;

            form.OnPointerMove(new Point(from.X + ((to.X - from.X) * t), from.Y + ((to.Y - from.Y) * t)));
        }

        form.OnPointerUp(to);
    }

    private static void Settle(Form form)
    {
        form.Clock.Advance(TimeSpan.FromSeconds(1));
        form.UpdateLayout();
    }

    [Fact]
    public void PullingDownAtTheTopRefreshes()
    {
        (Form form, RefreshContainer container, _, _) = Create();

        int requests = 0;
        container.RefreshRequested += (_, _) => requests++;

        Drag(form, new Point(200, 40), new Point(200, 240));

        Assert.Equal(1, requests);

        // nobody held it open: the refresh ended with the handler, and the content goes back
        Assert.Equal(RefreshState.Idle, container.State);

        Settle(form);
        Assert.Equal(0f, container.PullOffset);
    }

    [Fact]
    public void TheContentMovesWithThePullAndKnowsWhereItIs()
    {
        (Form form, RefreshContainer container, StackPanel list, _) = Create();

        Drag(form, new Point(200, 40), new Point(200, 140), release: false);

        Assert.InRange(container.PullOffset, 10f, 128f);
        Assert.Equal(container.PullOffset, list.GetAbsolutePosition().Y, 0.01f);
        Assert.Equal(RefreshState.Interacting, container.State);

        HeadlessInput.TouchCancel(form, 0);
    }

    [Fact]
    public void AShortPullSettlesBackWithoutRefreshing()
    {
        (Form form, RefreshContainer container, _, _) = Create();

        int requests = 0;
        container.RefreshRequested += (_, _) => requests++;

        Drag(form, new Point(200, 40), new Point(200, 75));

        Assert.Equal(0, requests);
        Assert.Equal(RefreshState.Idle, container.State);

        Settle(form);
        Assert.Equal(0f, container.PullOffset);
    }

    [Fact]
    public void ADeferralKeepsTheRefreshGoing()
    {
        (Form form, RefreshContainer container, _, _) = Create();

        RefreshDeferral? deferral = null;
        container.RefreshRequested += (_, e) => deferral = e.GetDeferral();

        Drag(form, new Point(200, 40), new Point(200, 240));
        Settle(form);

        Assert.True(container.IsRefreshing);
        Assert.True(container.HasPseudoClass(PseudoClass.Refreshing));

        // the content stays aside, the indicator resting in the gap
        Assert.Equal(container.RefreshThreshold, container.PullOffset);

        deferral!.Complete();

        Assert.False(container.IsRefreshing);
        Assert.False(container.HasPseudoClass(PseudoClass.Refreshing));

        Settle(form);
        Assert.Equal(0f, container.PullOffset);
    }

    [Fact]
    public void ADeferralCompletedOffTheUiThreadEndsTheRefreshOnIt()
    {
        (Form form, RefreshContainer container, _, HeadlessPlatform platform) = Create();

        RefreshDeferral? deferral = null;
        container.RefreshRequested += (_, e) => deferral = e.GetDeferral();

        container.RequestRefresh();

        Task.Run(() => deferral!.Complete()).Wait();

        // posted to the UI thread, not run on the pool's
        Assert.True(container.IsRefreshing);

        platform.PumpAll();

        Assert.False(container.IsRefreshing);
    }

    [Fact]
    public void ADeferralOfAnEndedRefreshDoesNotEndTheNextOne()
    {
        (_, RefreshContainer container, _, _) = Create();

        var deferrals = new List<RefreshDeferral>();
        container.RefreshRequested += (_, e) => deferrals.Add(e.GetDeferral());

        container.RequestRefresh();
        deferrals[0].Complete();

        container.RequestRefresh();
        Assert.True(container.IsRefreshing);

        // completing the first one again, or late, is nothing to the second
        deferrals[0].Complete();
        Assert.True(container.IsRefreshing);

        deferrals[1].Dispose();
        Assert.False(container.IsRefreshing);
    }

    [Fact]
    public void InTheMiddleOfTheListTheDragScrolls()
    {
        (Form form, RefreshContainer container, StackPanel list, _) = Create();

        list.ScrollTo(0, 400);
        form.UpdateLayout();

        int requests = 0;
        container.RefreshRequested += (_, _) => requests++;

        Drag(form, new Point(200, 40), new Point(200, 240));
        form.UpdateLayout();

        Assert.Equal(0, requests);
        Assert.InRange(list.ScrollY, 190f, 210f);
    }

    [Fact]
    public void DraggingUpAtTheTopScrollsTheList()
    {
        (Form form, RefreshContainer container, StackPanel list, _) = Create();

        int requests = 0;
        container.RefreshRequested += (_, _) => requests++;

        Drag(form, new Point(200, 250), new Point(200, 50));
        form.UpdateLayout();

        Assert.Equal(0, requests);
        Assert.InRange(list.ScrollY, 190f, 210f);
    }

    [Fact]
    public void ShortContentThatDoesNotScrollPullsToo()
    {
        (Form form, RefreshContainer container, _, _) = Create(rows: 2);

        int requests = 0;
        container.RefreshRequested += (_, _) => requests++;

        Drag(form, new Point(200, 40), new Point(200, 240));

        Assert.Equal(1, requests);
    }

    [Fact]
    public void BottomToTopPullsUpAtTheEnd()
    {
        (Form form, RefreshContainer container, StackPanel list, _) = Create();

        container.PullDirection = RefreshPullDirection.BottomToTop;

        int requests = 0;
        container.RefreshRequested += (_, _) => requests++;

        // at the top the upward drag is a scroll
        Drag(form, new Point(200, 260), new Point(200, 60));
        Assert.Equal(0, requests);

        list.ScrollTo(0, 10_000);
        form.UpdateLayout();

        Drag(form, new Point(200, 260), new Point(200, 60));
        Assert.Equal(1, requests);
    }

    [Fact]
    public void TheMousePullsOnlyWhenAllowedAndOnlyAtTheEdge()
    {
        (Form form, RefreshContainer container, StackPanel list, _) = Create();

        int requests = 0;
        container.RefreshRequested += (_, _) => requests++;

        MouseDrag(form, new Point(200, 40), new Point(200, 240));
        Assert.Equal(0, requests);

        container.AllowMousePull = true;

        list.ScrollTo(0, 400);
        form.UpdateLayout();

        // the mouse doesn't scroll the list, yet the list isn't at its top
        MouseDrag(form, new Point(200, 40), new Point(200, 240));
        Assert.Equal(0, requests);

        list.ScrollTo(0, 0);
        form.UpdateLayout();

        MouseDrag(form, new Point(200, 40), new Point(200, 240));
        Assert.Equal(1, requests);
    }

    [Fact]
    public void NoPullWhileRefreshing()
    {
        (Form form, RefreshContainer container, _, _) = Create();

        int requests = 0;
        container.RefreshRequested += (_, e) =>
        {
            requests++;
            e.GetDeferral();
        };

        container.RequestRefresh();
        Settle(form);

        Drag(form, new Point(200, 100), new Point(200, 280));

        Assert.Equal(1, requests);
        Assert.Equal(container.RefreshThreshold, container.PullOffset);
    }

    [Fact]
    public void F5RefreshesWhatHasTheFocus()
    {
        var button = new Button { Text = "Item" };
        var container = new RefreshContainer(new StackPanel { Children = { button } });

        var form = new Form { Size = new Size(400, 300), Content = container };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        int requests = 0;
        container.RefreshRequested += (_, _) => requests++;

        HeadlessInput.PressKey(form, Key.F5);
        Assert.Equal(0, requests);

        Assert.True(form.FocusForAccessibility(button));
        HeadlessInput.PressKey(form, Key.F5);

        Assert.Equal(1, requests);
    }

    [Fact]
    public void ThePeerIsBusyWhileRefreshingAndInvokeRefreshes()
    {
        (_, RefreshContainer container, _, _) = Create();

        RefreshDeferral? deferral = null;
        container.RefreshRequested += (_, e) => deferral = e.GetDeferral();

        AccessibilityPeer peer = container.GetAccessibilityPeer()!;

        Assert.True(peer.Actions.HasFlag(AccessibilityActions.Invoke));
        Assert.True(peer.Invoke());

        Assert.True(peer.States.HasFlag(AccessibilityStates.Busy));
        Assert.False(peer.Actions.HasFlag(AccessibilityActions.Invoke));

        deferral!.Complete();

        Assert.False(peer.States.HasFlag(AccessibilityStates.Busy));
    }

    [Fact]
    public void StateChangesAreReportedInOrder()
    {
        (Form form, RefreshContainer container, _, _) = Create();

        var states = new List<RefreshState>();
        container.StateChanged += (_, e) => states.Add(e.NewState);

        Drag(form, new Point(200, 40), new Point(200, 240));

        Assert.Equal(
            [RefreshState.Interacting, RefreshState.Pending, RefreshState.Refreshing, RefreshState.Idle],
            states);
    }
}