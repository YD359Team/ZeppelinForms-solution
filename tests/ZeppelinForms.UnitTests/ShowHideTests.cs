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

/// <summary>Hiding and showing an element with IsVisible: its panel is laid out
/// again either way, and EnterTransition and ExitTransition play as they do for an
/// added and a removed element.</summary>
[Collection("Platform")]
public class ShowHideTests
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

        protected override Size MeasureOverride(Size availableSize) => ResolveSize(new Size(100, 40), availableSize);
    }

    private static (Form Form, StackPanel Panel, ProbeBox Card) CreateForm(
        VisibilityTransition? enter = null, VisibilityTransition? exit = null)
    {
        var card = new ProbeBox { EnterTransition = enter, ExitTransition = exit };

        // the panel sizes itself to its content, as the demo's column does
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Top,
            Children = { new ProbeBox(), card },
        };

        // inside an outer column, which asks for the content's own height
        var outer = new StackPanel { Orientation = Orientation.Vertical, Children = { panel } };

        var form = new Form { Size = new Size(400, 300), Content = outer };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return (form, panel, card);
    }

    private static void Render(Form form) =>
        ElementTreeRenderer.Draw(form.Content!, new HeadlessGraphics());

    [Fact]
    public void AShownElementIsLaidOutAgain()
    {
        var (form, panel, card) = CreateForm();
        float full = panel.ActualSize.Height;

        card.IsVisible = false;
        form.UpdateLayout();
        Assert.Equal(full - 40, panel.ActualSize.Height, 0.5f);

        // the panel used to stay measured without it: the hidden card kept its
        // stale measure mark, and the mark stopped the walk up to the panel
        card.IsVisible = true;
        form.UpdateLayout();
        Assert.Equal(full, panel.ActualSize.Height, 0.5f);

        int before = card.Draws;
        Render(form);
        Assert.Equal(before + 1, card.Draws);
    }

    [Fact]
    public void AChangeInsideAHiddenElementReachesThePanelWhenShown()
    {
        var (form, panel, card) = CreateForm();
        float full = panel.ActualSize.Height;

        card.IsVisible = false;
        form.UpdateLayout();

        card.Size = new Size(100, 90);
        card.IsVisible = true;
        form.UpdateLayout();

        Assert.Equal(full + 50, panel.ActualSize.Height, 0.5f);
    }

    [Fact]
    public void ShowingPlaysTheEnterTransition()
    {
        var (form, _, card) = CreateForm(enter: VisibilityTransition.Fade(200, Easing.Linear));

        card.IsVisible = false;
        form.UpdateLayout();
        card.IsVisible = true;
        form.UpdateLayout();

        form.Clock.Advance(TimeSpan.FromMilliseconds(100));
        Render(form);
        Assert.InRange(card.SeenOpacity, 0.4f, 0.6f);

        form.Clock.Advance(TimeSpan.FromMilliseconds(100));
        Render(form);
        Assert.Equal(1f, card.SeenOpacity);
    }

    [Fact]
    public void HidingPlaysTheExitTransition()
    {
        var (form, _, card) = CreateForm(exit: VisibilityTransition.Fade(200, Easing.Linear));
        Render(form);

        card.IsVisible = false;
        form.UpdateLayout();

        // hidden, but still drawn while it fades
        int before = card.Draws;
        form.Clock.Advance(TimeSpan.FromMilliseconds(100));
        Render(form);
        Assert.True(card.Draws > before);

        // the fade ended — it is no longer drawn
        form.Clock.Advance(TimeSpan.FromMilliseconds(150));
        int after = card.Draws;
        Render(form);
        Assert.Equal(after, card.Draws);
    }

    [Fact]
    public void ShownBeforeTheExitEndsItIsDrawnOnce()
    {
        var (form, _, card) = CreateForm(exit: VisibilityTransition.Fade(200, Easing.Linear));
        Render(form);

        card.IsVisible = false;
        form.UpdateLayout();
        card.IsVisible = true;
        form.UpdateLayout();

        int before = card.Draws;
        Render(form);
        Assert.Equal(before + 1, card.Draws);
    }

    [Fact]
    public void WithoutRulesNothingPlays()
    {
        var (form, _, card) = CreateForm();
        Render(form);

        card.IsVisible = false;
        form.UpdateLayout();

        int before = card.Draws;
        Render(form);
        Assert.Equal(before, card.Draws);

        card.IsVisible = true;
        form.UpdateLayout();
        Render(form);
        Assert.Equal(1f, card.SeenOpacity);
    }
}
