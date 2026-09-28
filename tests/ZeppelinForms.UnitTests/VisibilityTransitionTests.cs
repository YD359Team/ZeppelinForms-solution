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

[Collection("Platform")]
public class VisibilityTransitionTests
{
    private sealed class ProbeBox : UnitControl
    {
        public int Draws { get; private set; }

        public float SeenOpacity { get; private set; } = float.NaN;

        public override void Draw(Graphics g)
        {
            Draws++;
            SeenOpacity = Opacity;
        }

        protected override Size MeasureOverride(Size availableSize) => new(100, 40);
    }

    private static (Form Form, StackPanel Panel, ProbeBox First) CreateForm()
    {
        var platform = new HeadlessPlatform();

        var first = new ProbeBox();

        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ChildrenEnterTransition = VisibilityTransition.Fade(200, Easing.Linear),
            ChildrenExitTransition = VisibilityTransition.Fade(200, Easing.Linear),
        };

        panel.Children.Add(first);

        var form = new Form { Size = new Size(400, 300), Content = panel };
        platform.CreateWindow(form);
        form.UpdateLayout();

        return (form, panel, first);
    }

    private static void Render(Form form) =>
        ElementTreeRenderer.Draw(form.Content!, new HeadlessGraphics());

    [Fact]
    public void InitialContentDoesNotFadeIn()
    {
        var (form, _, first) = CreateForm();

        Render(form);

        // opening a form is not an appearance: the first content is visible right away
        Assert.Equal(1f, first.SeenOpacity);
    }

    [Fact]
    public void AddedChildFadesIn()
    {
        var (form, panel, _) = CreateForm();

        var added = new ProbeBox();
        panel.Children.Add(added);
        form.UpdateLayout();

        // at the very start the element is fully transparent — the renderer doesn't
        // even draw such, so the first check is made halfway
        Render(form);
        Assert.True(float.IsNaN(added.SeenOpacity), "fully transparent is not drawn");

        form.Clock.Advance(TimeSpan.FromMilliseconds(100));
        Render(form);
        Assert.InRange(added.SeenOpacity, 0.4f, 0.6f);

        form.Clock.Advance(TimeSpan.FromMilliseconds(100));
        Render(form);
        Assert.Equal(1f, added.SeenOpacity);

        // the model saw the final value all this time
        Assert.Equal(1f, added.Opacity);
    }

    [Fact]
    public void RemovedChildIsDrawnUntilExitEnds()
    {
        var (form, panel, first) = CreateForm();

        Render(form);

        panel.Children.Remove(first);
        form.UpdateLayout();

        int before = first.Draws;

        // removed from the panel, but still on screen
        form.Clock.Advance(TimeSpan.FromMilliseconds(100));
        Render(form);

        Assert.True(first.Draws > before);

        // the disappearance ended — it is no longer drawn
        form.Clock.Advance(TimeSpan.FromMilliseconds(150));

        int after = first.Draws;
        Render(form);

        Assert.Equal(after, first.Draws);
    }

    [Fact]
    public void ReaddedChildIsNotDrawnTwice()
    {
        var (form, panel, first) = CreateForm();

        Render(form);

        panel.Children.Remove(first);
        panel.Children.Add(first);
        form.UpdateLayout();

        int before = first.Draws;
        Render(form);

        // brought back in the middle of disappearing: drawn once, alive
        Assert.Equal(before + 1, first.Draws);
    }

    [Fact]
    public void MoveIsNotAnExit()
    {
        var (form, panel, first) = CreateForm();

        var second = new ProbeBox();
        panel.Children.Add(second);
        form.UpdateLayout();

        // let the second one's appearance finish, so it doesn't interfere with the count
        form.Clock.Advance(TimeSpan.FromMilliseconds(250));
        Render(form);

        panel.Children.Move(0, 1);
        form.UpdateLayout();

        int before = first.Draws;
        Render(form);

        Assert.Equal(before + 1, first.Draws);
    }
}