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
public class TransitionTests
{
    /// <summary>Запоминает, каким увидела свойство отрисовка.</summary>
    private sealed class ProbeBox : UnitControl
    {
        public float SeenRotation { get; private set; } = float.NaN;

        public override void Draw(Graphics g) => SeenRotation = Rotation;

        protected override Size MeasureOverride(Size availableSize) => new(100, 40);
    }

    private static (Form Form, ProbeBox Box) CreateForm()
    {
        var platform = new HeadlessPlatform();

        var box = new ProbeBox();

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

        box.Transitions.Add(Transition.Ease(UIElement.RotationProperty, 200, Easing.Linear));

        return (form, box);
    }

    private static void Render(Form form) =>
        ElementTreeRenderer.Draw(form.Content!, new HeadlessGraphics());

    [Fact]
    public void ReadingPropertyReturnsTargetImmediately()
    {
        var (_, box) = CreateForm();

        box.Rotation = 90f;

        // модель мгновенна: код и биндинги видят цель, а не полпути
        Assert.Equal(90f, box.Rotation);
    }

    [Fact]
    public void DrawingSeesIntermediateValue()
    {
        var (form, box) = CreateForm();

        box.Rotation = 90f;

        form.Clock.Advance(TimeSpan.FromMilliseconds(100));
        Render(form);

        // половина перехода при линейной кривой — примерно половина пути
        Assert.InRange(box.SeenRotation, 30f, 60f);
    }

    [Fact]
    public void TransitionEndsAtTarget()
    {
        var (form, box) = CreateForm();

        box.Rotation = 90f;

        form.Clock.Advance(TimeSpan.FromMilliseconds(200));
        Render(form);

        Assert.Equal(90f, box.SeenRotation);

        // переход закончился — дальше отрисовка читает саму цель
        form.Clock.Advance(TimeSpan.FromMilliseconds(50));
        Render(form);

        Assert.Equal(90f, box.SeenRotation);
    }

    [Fact]
    public void RetargetingContinuesFromCurrentValue()
    {
        var (form, box) = CreateForm();

        box.Rotation = 90f;

        form.Clock.Advance(TimeSpan.FromMilliseconds(100));
        Render(form);

        float midway = box.SeenRotation;

        // цель поменялась на полпути: новый переход начинается отсюда же,
        // а не прыгает к прежнему началу
        box.Rotation = 0f;
        Render(form);

        Assert.InRange(box.SeenRotation, midway - 2f, midway + 2f);
    }

    [Fact]
    public void DisabledTransitionsApplyImmediately()
    {
        var (form, box) = CreateForm();

        bool before = UIElement.TransitionsEnabled;
        UIElement.TransitionsEnabled = false;

        try
        {
            box.Rotation = 90f;
            Render(form);

            Assert.Equal(90f, box.SeenRotation);
        }
        finally
        {
            UIElement.TransitionsEnabled = before;
        }
    }
}