using Xunit;
using ZeppelinForms.Animation;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>What nobody can see asks for no frames: an endless animation scrolled out
/// of view, and every animation of a window minimized or hidden. A finite one out of
/// view still runs to its end — its completion may be what tidies the state up.</summary>
[Collection("Platform")]
public sealed class OutOfSightFramesTests
{
    private sealed class ProbeBox : UnitControl
    {
        public override void Draw(Graphics g) { }

        protected override Size MeasureOverride(Size availableSize) => new(100, 40);
    }

    /// <summary>Fifty rows of 40 in a window 200 high: the last one is far below the view.</summary>
    private static (Form Form, StackPanel Panel, ProbeBox First, ProbeBox Last) CreateList()
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, OverflowY = Overflow.Auto };

        for (int i = 0; i < 50; i++)
            panel.Children.Add(new ProbeBox());

        var form = new Form { Size = new Size(300, 200), Content = panel };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return (form, panel, (ProbeBox)panel.Children[0], (ProbeBox)panel.Children[panel.Children.Count - 1]);
    }

    private static bool FramesRun(Form form) => form.PlatformWindow!.Frames.IsRunning;

    [Fact]
    public void AnEndlessAnimationOutOfViewAsksForNoFrames()
    {
        var (form, _, _, last) = CreateList();
        int steps = 0;

        last.AnimateLoop("spin", TimeSpan.FromSeconds(1), _ => steps++);

        Assert.False(FramesRun(form));

        form.Clock.Advance(TimeSpan.FromMilliseconds(16));
        Assert.Equal(0, steps);
    }

    [Fact]
    public void AnEndlessAnimationInViewRuns()
    {
        var (form, _, first, _) = CreateList();
        int steps = 0;

        first.AnimateLoop("spin", TimeSpan.FromSeconds(1), _ => steps++);

        Assert.True(FramesRun(form));

        form.Clock.Advance(TimeSpan.FromMilliseconds(16));
        Assert.Equal(1, steps);
    }

    [Fact]
    public void ScrolledIntoViewTheAnimationGetsItsFramesBack()
    {
        var (form, panel, _, last) = CreateList();
        int steps = 0;

        last.AnimateLoop("spin", TimeSpan.FromSeconds(1), _ => steps++);
        Assert.False(FramesRun(form));

        // a scroll is laid out and drawn; the frame drawn reviews the frames
        panel.ScrollTo(0, float.MaxValue);
        form.UpdateLayout();
        FormRenderer.Render(form, new HeadlessGraphics());

        Assert.True(FramesRun(form));

        form.Clock.Advance(TimeSpan.FromMilliseconds(16));
        Assert.Equal(1, steps);
    }

    [Fact]
    public void ScrolledOutOfViewTheAnimationStands()
    {
        var (form, panel, first, _) = CreateList();
        int steps = 0;

        first.AnimateLoop("spin", TimeSpan.FromSeconds(1), _ => steps++);
        Assert.True(FramesRun(form));

        panel.ScrollTo(0, float.MaxValue);
        form.UpdateLayout();
        FormRenderer.Render(form, new HeadlessGraphics());

        Assert.False(FramesRun(form));

        form.Clock.Advance(TimeSpan.FromMilliseconds(16));
        Assert.Equal(0, steps);
    }

    [Fact]
    public void AFiniteAnimationOutOfViewRunsToItsEnd()
    {
        var (form, _, _, last) = CreateList();
        bool completed = false;

        last.Animate(
            "slide", 0f, 1f, TimeSpan.FromMilliseconds(100),
            (from, to, t) => from + (to - from) * t,
            _ => { },
            completed: () => completed = true);

        Assert.True(FramesRun(form));

        form.Clock.Advance(TimeSpan.FromMilliseconds(100));

        Assert.True(completed);
        Assert.False(FramesRun(form));
    }

    [Fact]
    public void AnElementOfZeroOpacityIsOutOfView()
    {
        var (form, _, first, _) = CreateList();

        first.Opacity = 0f;
        first.AnimateLoop("spin", TimeSpan.FromSeconds(1), _ => { });

        Assert.False(FramesRun(form));
    }

    [Fact]
    public void AWindowOutOfSightAsksForNoFramesUntilItIsBack()
    {
        var (form, _, first, _) = CreateList();
        int steps = 0;

        first.AnimateLoop("spin", TimeSpan.FromSeconds(1), _ => steps++);
        Assert.True(FramesRun(form));

        // minimized, unmapped, cloaked: the platform says so
        form.SetWindowOutOfSight(true);
        Assert.False(FramesRun(form));

        form.SetWindowOutOfSight(false);
        Assert.True(FramesRun(form));
    }

    [Fact]
    public void AHiddenFormAsksForNoFramesUntilShown()
    {
        var box = new ProbeBox();
        var form = new Form { Size = new Size(300, 200), Content = new StackPanel { Children = { box } } };
        new HeadlessPlatform().CreateWindow(form);
        form.Show();
        form.UpdateLayout();

        box.AnimateLoop("spin", TimeSpan.FromSeconds(1), _ => { });
        Assert.True(FramesRun(form));

        form.Hide();
        Assert.False(FramesRun(form));

        form.Show();
        Assert.True(FramesRun(form));
    }

    [Fact]
    public void AnotherWindowStartsInSight()
    {
        var box = new ProbeBox();
        var form = new Form { Size = new Size(300, 200), Content = new StackPanel { Children = { box } } };
        var platform = new HeadlessPlatform();

        platform.CreateWindow(form);
        form.SetWindowOutOfSight(true);

        // the form closed while minimized and opened again: the new window's sight
        // is its own
        platform.CreateWindow(form);
        form.UpdateLayout();

        box.AnimateLoop("spin", TimeSpan.FromSeconds(1), _ => { });
        Assert.True(FramesRun(form));
    }
}
