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

    /// <summary>Проведение мышью: свайп работает и ей, распознаватель
    /// различает палец и мышь только порогами.</summary>
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

        // контакт, забранный жестом, обязан закончиться вместе с отпусканием:
        // иначе он остаётся в конвейере, следующее нажатие принимается
        // за вторую кнопку того же контакта, и арена заново не собирается
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

        // наведение считается по тому, кто под курсором, — а при живом
        // контакте движение уходило бы цели прошлого нажатия
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