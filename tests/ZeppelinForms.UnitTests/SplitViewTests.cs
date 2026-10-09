using Xunit;
using ZeppelinForms.Accessibility;
using ZeppelinForms.Animation;
using ZeppelinForms.Core.Globalization;
using ZeppelinForms.Core.Text;
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
public class SplitViewTests
{
    private const float Width = 600;
    private const float Height = 400;

    private sealed class Block : UnitControl
    {
        public Block()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Stretch;
        }

        public override void Draw(Graphics g) { }
    }

    private static (Form Form, SplitView View, Block Pane, Block Content) Create(
        SplitViewDisplayMode mode = SplitViewDisplayMode.Overlay)
    {
        var pane = new Block();
        var content = new Block();

        var view = new SplitView
        {
            DisplayMode = mode,
            Pane = pane,
            Content = content,
        };

        var form = new Form { Size = new Size(Width, Height), Content = view };

        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return (form, view, pane, content);
    }

    /// <summary>Let the pane finish moving and lay the form out.</summary>
    private static void Settle(Form form)
    {
        form.Clock.Advance(TimeSpan.FromSeconds(1));
        form.UpdateLayout();
    }

    [Fact]
    public void AClosedOverlayPaneIsHiddenAndTheContentTakesTheWidth()
    {
        (_, SplitView view, Block pane, Block content) = Create();

        Assert.False(view.IsPaneOpen);
        Assert.False(pane.IsEffectivelyVisible);
        Assert.Equal(0f, content.Position.X);
        Assert.Equal(Width, content.ActualSize.Width);
    }

    [Fact]
    public void AnOverlayPaneOpensOverTheContent()
    {
        (Form form, SplitView view, Block pane, Block content) = Create();

        int opened = 0;
        view.PaneOpened += (_, _) => opened++;

        view.IsPaneOpen = true;

        // the opening plays out: nothing is open yet
        Assert.Equal(0, opened);

        Settle(form);

        Assert.Equal(1, opened);
        Assert.Equal(320f, view.PaneBounds.Size.Width);

        // over the content, not beside it: the content keeps its place
        Assert.Equal(0f, content.Position.X);
        Assert.Equal(Width, content.ActualSize.Width);
        Assert.True(view.HasPseudoClass(PseudoClass.Open));
        Assert.True(view.HasPseudoClass(PseudoClass.Overlay));
    }

    [Fact]
    public void TheOpeningMovesThroughTheWidthsBetween()
    {
        (Form form, SplitView view, _, _) = Create();

        view.IsPaneOpen = true;

        form.Clock.Advance(TimeSpan.FromMilliseconds(60));
        form.UpdateLayout();

        Assert.InRange(view.PaneBounds.Size.Width, 1f, 319f);
    }

    [Fact]
    public void AnInlinePanePushesTheContentAside()
    {
        (Form form, SplitView view, _, Block content) = Create(SplitViewDisplayMode.Inline);

        view.IsPaneOpen = true;
        Settle(form);

        Assert.Equal(320f, content.Position.X);
        Assert.Equal(Width - 320f, content.ActualSize.Width);
        Assert.False(view.HasPseudoClass(PseudoClass.Overlay));
    }

    [Fact]
    public void ACompactPaneKeepsAStripOfThePaneLaidOutAtItsFullWidth()
    {
        (_, SplitView view, Block pane, Block content) = Create(SplitViewDisplayMode.CompactOverlay);

        Assert.True(pane.IsEffectivelyVisible);
        Assert.Equal(48f, view.PaneBounds.Size.Width);

        // the strip is the edge of the same pane: nothing in it reflows on opening
        Assert.Equal(320f, pane.ActualSize.Width);

        Assert.Equal(48f, content.Position.X);
        Assert.Equal(Width - 48f, content.ActualSize.Width);
        Assert.True(view.HasPseudoClass(PseudoClass.Compact));
    }

    [Fact]
    public void APaneAtTheEndIsOnTheRightAndShowsItsOuterEdge()
    {
        (Form form, SplitView view, Block pane, Block content) = Create(SplitViewDisplayMode.CompactInline);

        view.PanePlacement = SplitViewPanePlacement.End;
        form.UpdateLayout();

        Assert.Equal(Width - 48f, view.PaneBounds.Position.X);
        Assert.Equal(0f, content.Position.X);

        // anchored to the window's edge: the strip shows the pane's right column
        Assert.Equal(48f - 320f, pane.Position.X);
    }

    [Fact]
    public void RightToLeftPutsTheStartOnTheRight()
    {
        (Form form, SplitView view, _, _) = Create(SplitViewDisplayMode.CompactInline);

        view.FlowDirection = FlowDirection.RightToLeft;
        form.UpdateLayout();

        Assert.Equal(Width - 48f, view.PaneBounds.Position.X);
    }

    [Fact]
    public void AClickOnTheContentDismissesAnOverlayPane()
    {
        (Form form, SplitView view, _, _) = Create();

        view.IsPaneOpen = true;
        Settle(form);

        // past the pane, on the content under the scrim
        HeadlessInput.Click(form, 450, 200);

        Assert.False(view.IsPaneOpen);
    }

    [Fact]
    public void AClickInThePaneLeavesItOpen()
    {
        (Form form, SplitView view, _, _) = Create();

        view.IsPaneOpen = true;
        Settle(form);

        HeadlessInput.Click(form, 100, 200);

        Assert.True(view.IsPaneOpen);
    }

    [Fact]
    public void AnInlinePaneIsNotDismissedByAClick()
    {
        (Form form, SplitView view, _, _) = Create(SplitViewDisplayMode.Inline);

        view.IsPaneOpen = true;
        Settle(form);

        HeadlessInput.Click(form, 450, 200);

        Assert.True(view.IsPaneOpen);
    }

    [Fact]
    public void PaneClosingCanKeepThePaneOpen()
    {
        (Form form, SplitView view, _, _) = Create();

        view.IsPaneOpen = true;
        Settle(form);

        view.PaneClosing += (_, e) => e.Cancel = true;
        view.IsPaneOpen = false;

        Assert.True(view.IsPaneOpen);
        Assert.True(view.HasPseudoClass(PseudoClass.Open));
    }

    [Fact]
    public void AReversedOpeningDoesNotReportTheSideItNeverReached()
    {
        (Form form, SplitView view, _, _) = Create();

        int opened = 0, closed = 0;
        view.PaneOpened += (_, _) => opened++;
        view.PaneClosed += (_, _) => closed++;

        view.IsPaneOpen = true;
        form.Clock.Advance(TimeSpan.FromMilliseconds(60));

        view.IsPaneOpen = false;
        Settle(form);

        Assert.Equal(0, opened);
        Assert.Equal(1, closed);
        Assert.Equal(0f, view.PaneBounds.Size.Width);
    }

    [Fact]
    public void UnderReducedMotionThePaneOpensAtOnce()
    {
        MotionPreference before = Motion.Preference;
        Motion.Preference = MotionPreference.Reduced;

        try
        {
            (Form form, SplitView view, _, _) = Create();

            int opened = 0;
            view.PaneOpened += (_, _) => opened++;

            view.IsPaneOpen = true;
            form.UpdateLayout();

            Assert.Equal(1, opened);
            Assert.Equal(320f, view.PaneBounds.Size.Width);
        }
        finally
        {
            Motion.Preference = before;
        }
    }

    [Fact]
    public void AStyleSheetSelectsByTheNewPseudoClasses()
    {
        (Form form, SplitView view, _, _) = Create(SplitViewDisplayMode.CompactOverlay);

        StyleSheet sheet = StyleSheet.Parse(
            "SplitView:compact { CompactPaneLength: 64; } SplitView:open { PaneBackground: #FF0000; }",
            "test.zss");

        Assert.Empty(sheet.Diagnostics);
        form.Styles.Add(sheet);
        form.UpdateLayout();

        Assert.Equal(64f, view.PaneBounds.Size.Width);
        Assert.NotEqual(new Color(255, 255, 0, 0), view.PaneBackground);

        view.IsPaneOpen = true;
        Settle(form);

        Assert.Equal(new Color(255, 255, 0, 0), view.PaneBackground);
    }

    // ===== focus and keyboard =====

    private static (Form Form, SplitView View, Button InPane, Button InContent) CreateWithButtons(
        SplitViewDisplayMode mode = SplitViewDisplayMode.Overlay)
    {
        var inPane = new Button { Text = "Home" };
        var inContent = new Button { Text = "Menu" };

        var view = new SplitView
        {
            DisplayMode = mode,
            Pane = new StackPanel { Children = { inPane } },
            Content = new StackPanel { Children = { inContent } },
        };

        var form = new Form { Size = new Size(Width, Height), Content = view };

        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return (form, view, inPane, inContent);
    }

    [Fact]
    public void AnOverlayPaneTakesTheFocusAndEscapeGivesItBack()
    {
        (Form form, SplitView view, Button inPane, Button inContent) = CreateWithButtons();

        Assert.True(form.FocusForAccessibility(inContent));

        view.IsPaneOpen = true;

        Assert.True(inPane.IsFocused);

        HeadlessInput.PressKey(form, Key.Escape);

        Assert.False(view.IsPaneOpen);
        Assert.True(inContent.IsFocused);
    }

    [Fact]
    public void APaneOpenedFromElsewhereLeavesTheFocusAlone()
    {
        (_, SplitView view, Button inPane, _) = CreateWithButtons();

        view.IsPaneOpen = true;

        Assert.False(inPane.IsFocused);
    }

    [Fact]
    public void TabGoesThroughThePaneFirst()
    {
        (Form form, _, Button inPane, Button inContent) = CreateWithButtons(SplitViewDisplayMode.CompactInline);

        HeadlessInput.PressKey(form, Key.Tab);
        Assert.True(inPane.IsFocused);

        HeadlessInput.PressKey(form, Key.Tab);
        Assert.True(inContent.IsFocused);
    }

    [Fact]
    public void TabSkipsAPaneClosedToNothing()
    {
        (Form form, _, _, Button inContent) = CreateWithButtons();

        HeadlessInput.PressKey(form, Key.Tab);

        Assert.True(inContent.IsFocused);
    }

    // ===== accessibility =====

    [Fact]
    public void ThePeerExpandsAndCollapsesThePane()
    {
        (Form form, SplitView view, _, _) = Create();

        AccessibilityPeer peer = view.GetAccessibilityPeer()!;

        Assert.Equal(AccessibilityRole.Group, peer.Role);
        Assert.True(peer.States.HasFlag(AccessibilityStates.Collapsed));

        // a pane closed to nothing is not in the tree: only the content
        Assert.Single(peer.Children);

        Assert.True(peer.Expand());
        Settle(form);

        Assert.True(view.IsPaneOpen);
        Assert.True(peer.States.HasFlag(AccessibilityStates.Expanded));

        // the pane comes first, named for what it is
        Assert.Equal(2, peer.Children.Count);
        Assert.Equal(Localization.Get(ZfText.Pane), peer.Children[0].Name);

        view.PaneTitle = "Navigation";
        Assert.Equal("Navigation", peer.Children[0].Name);

        Assert.True(peer.Collapse());
        Assert.False(view.IsPaneOpen);
    }
}