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
public class TransformTests
{
    private sealed class ProbeBox : UnitControl
    {
        public int Draws { get; private set; }

        public float SeenTranslateX { get; private set; }

        public override void Draw(Graphics g)
        {
            Draws++;
            SeenTranslateX = TranslateX;
        }

        protected override Size MeasureOverride(Size availableSize) => new(100, 40);
    }

    private static (Form Form, StackPanel Panel, ProbeBox Box) CreateForm()
    {
        var platform = new HeadlessPlatform();

        var box = new ProbeBox
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        panel.Children.Add(box);

        var form = new Form { Size = new Size(400, 300), Content = panel };
        platform.CreateWindow(form);
        form.UpdateLayout();

        return (form, panel, box);
    }

    [Fact]
    public void TranslateDoesNotMoveLayout()
    {
        var (form, _, box) = CreateForm();

        Point before = box.Position;

        box.TranslateX = 50f;
        box.TranslateY = 20f;
        form.UpdateLayout();

        // the offset is a drawing property: layout doesn't know about it
        Assert.Equal(before, box.Position);
    }

    [Fact]
    public void HitTestFollowsTranslate()
    {
        var (form, _, box) = CreateForm();

        bool hitAtOrigin = false;

        box.MouseDown += (_, _) => hitAtOrigin = true;

        box.TranslateX = 120f;
        form.UpdateLayout();

        // where the element was by layout, it is no longer
        HeadlessInput.Click(form, 10, 10);
        Assert.False(hitAtOrigin);

        // and where it was moved to, it is
        HeadlessInput.Click(form, 130, 10);
        Assert.True(hitAtOrigin);
    }

    [Fact]
    public void HitTestFollowsScale()
    {
        var (form, _, box) = CreateForm();

        bool hit = false;

        box.MouseDown += (_, _) => hit = true;

        // twice as wide from the center: the right edge moves from 100 to 150
        box.ScaleX = 2f;
        form.UpdateLayout();

        HeadlessInput.Click(form, 140, 20);

        Assert.True(hit);
    }

    [Fact]
    public void TranslateKeepsCulling()
    {
        var (form, panel, box) = CreateForm();

        // take the element far beyond the window — there is no point drawing it
        box.TranslateY = 1000f;
        form.UpdateLayout();

        int before = box.Draws;

        ElementTreeRenderer.Draw(panel, new HeadlessGraphics());

        Assert.Equal(before, box.Draws);
    }

    [Fact]
    public void TranslateIsTransitionable()
    {
        var (form, panel, box) = CreateForm();

        box.Transitions.Add(Transition.Ease(UIElement.TranslateXProperty, 200, Easing.Linear));

        box.TranslateX = 100f;

        form.Clock.Advance(TimeSpan.FromMilliseconds(100));
        ElementTreeRenderer.Draw(panel, new HeadlessGraphics());

        // layout is still untouched, while the picture is already halfway
        Assert.InRange(box.SeenTranslateX, 30f, 70f);
        Assert.Equal(100f, box.TranslateX);
    }
}