using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Gestures;

namespace ZeppelinForms.UnitTests;

[Collection("Platform")]
public class PointerContactTests
{
    private static (Form Form, Button Button, List<SwipeDirection> Swipes) CreateForm()
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

        var swipe = new SwipeGestureRecognizer();
        List<SwipeDirection> swipes = [];

        swipe.Swiped += (_, args) => swipes.Add(args.Direction);
        panel.GestureRecognizers.Add(swipe);

        var form = new Form { Size = new Size(400, 300), Content = panel };
        platform.CreateWindow(form);
        form.UpdateLayout();

        return (form, button, swipes);
    }

    /// <summary>Dragging with the mouse: the swipe works for it too, the recognizer
    /// tells a finger from the mouse only by thresholds.</summary>
    private static void MouseSwipe(Form form, Point from, Point to, int steps = 8)
    {
        form.OnPointerDown(from);

        for (int i = 1; i <= steps; i++)
        {
            float t = (float)i / steps;

            form.OnPointerMove(new Point(
                from.X + ((to.X - from.X) * t),
                from.Y + ((to.Y - from.Y) * t)));
        }

        form.OnPointerUp(to);
    }

    [Fact]
    public void MouseSwipeIsRecognized()
    {
        var (form, _, swipes) = CreateForm();

        MouseSwipe(form, new Point(300, 150), new Point(40, 150));

        Assert.Equal([SwipeDirection.Left], swipes);
    }

    [Fact]
    public void SwipeRepeatsAfterItself()
    {
        var (form, _, swipes) = CreateForm();

        // a contact taken by a gesture must end together with the release:
        // otherwise it stays in the pipeline, the next press is taken
        // for a second button of the same contact, and the arena isn't assembled again
        MouseSwipe(form, new Point(300, 150), new Point(40, 150));
        MouseSwipe(form, new Point(300, 150), new Point(40, 150));

        Assert.Equal(2, swipes.Count);
    }

    [Fact]
    public void ClicksKeepWorkingAfterSwipe()
    {
        var (form, button, _) = CreateForm();

        int clicks = 0;
        button.Click += (_, _) => clicks++;

        MouseSwipe(form, new Point(300, 150), new Point(40, 150));

        HeadlessInput.Click(form, 100, 12);

        Assert.Equal(1, clicks);
    }

    [Fact]
    public void HoverKeepsWorkingAfterSwipe()
    {
        var (form, button, _) = CreateForm();

        bool entered = false;
        button.MouseEnter += (_, _) => entered = true;

        MouseSwipe(form, new Point(300, 150), new Point(40, 150));

        HeadlessInput.MoveMouse(form, 100, 12);

        // hover is computed from whoever is under the cursor — while with a live
        // contact the movement would go to the target of the previous press
        Assert.True(entered);
    }

    [Fact]
    public void TouchSwipeAlsoEndsItsContact()
    {
        var (form, _, swipes) = CreateForm();

        HeadlessInput.Swipe(form, new Point(300, 150), new Point(40, 150));
        HeadlessInput.Swipe(form, new Point(300, 150), new Point(40, 150));

        Assert.Equal(2, swipes.Count);
    }
}