using Xunit;
using ZeppelinForms.Animation;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.UnitTests;

[Collection("Platform")]
public class MouseMoveAndFramesTests
{
    private static (Form Form, Button Button) CreateForm()
    {
        var platform = new HeadlessPlatform();

        var button = new Button
        {
            Text = "кнопка",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        panel.Children.Add(button);

        var form = new Form { Size = new Size(400, 300), Content = panel };
        platform.CreateWindow(form);
        form.UpdateLayout();

        return (form, button);
    }

    [Fact]
    public void HoverCarriesNoButtonsAndDragCarriesLeft()
    {
        var (form, button) = CreateForm();
        List<PointerButtons> seen = [];

        button.MouseMove += (_, args) => seen.Add(args.Buttons);

        form.OnPointerMove(new Point(100, 10));

        form.OnPointerDown(new Point(100, 10));
        form.OnPointerMove(new Point(110, 12));
        form.OnPointerUp(new Point(110, 12));

        Assert.Equal([PointerButtons.None, PointerButtons.Left], seen);
    }

    [Fact]
    public void MoveCarriesModifiersFromThePlatform()
    {
        var (form, button) = CreateForm();
        KeyModifiers? seen = null;

        button.MouseMove += (_, args) => seen = args.Modifiers;

        form.OnPointerMove(new Point(100, 10), KeyModifiers.Shift | KeyModifiers.Control);

        Assert.Equal(KeyModifiers.Shift | KeyModifiers.Control, seen);
    }

    [Fact]
    public void FramesGetTheStepAndStopWhenTheCallbackSaysSo()
    {
        var (form, button) = CreateForm();
        List<TimeSpan> steps = [];

        button.AnimateFrames("frames", step =>
        {
            steps.Add(step);
            return steps.Count < 3;
        });

        for (int i = 0; i < 5; i++)
            form.Clock.Advance(TimeSpan.FromMilliseconds(16));

        Assert.Equal(3, steps.Count);
        Assert.All(steps, step => Assert.Equal(TimeSpan.FromMilliseconds(16), step));

        // the loop is over — the window no longer needs frames for it
        Assert.False(form.PlatformWindow!.Frames.IsRunning);
    }

    [Fact]
    public void StopAnimationEndsTheLoopFromOutside()
    {
        var (form, button) = CreateForm();
        int calls = 0;

        button.AnimateFrames("frames", _ => { calls++; return true; });

        form.Clock.Advance(TimeSpan.FromMilliseconds(16));
        button.StopAnimation("frames");
        form.Clock.Advance(TimeSpan.FromMilliseconds(16));

        Assert.Equal(1, calls);
    }
}