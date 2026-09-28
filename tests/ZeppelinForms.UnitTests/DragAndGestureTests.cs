using Xunit;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Gestures;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.UnitTests;

[Collection("Platform")]
public class DragAndGestureTests
{
    /// <summary>Takes the pointer for itself on press — the same way
    /// DragList, the panel's scrollbar thumb and TrackBar do.</summary>
    private sealed class CapturingBox : UnitControl
    {
        public override void Draw(Graphics g) { }

        protected override Size MeasureOverride(Size availableSize) => new(200, 100);

        protected override void OnMouseDown(MouseButtonEventArgs e) => CaptureMouse();

        protected override void OnMouseUp(MouseButtonEventArgs e) => ReleaseMouseCapture();
    }

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
    public void CaptureEndsGestureCompetition()
    {
        var platform = new HeadlessPlatform();

        var box = new CapturingBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        panel.Children.Add(box);

        var swipe = new SwipeGestureRecognizer();
        int swipes = 0;

        swipe.Swiped += (_, _) => swipes++;
        panel.GestureRecognizers.Add(swipe);

        var form = new Form { Size = new Size(400, 300), Content = panel };
        platform.CreateWindow(form);
        form.UpdateLayout();

        // the same movement that is recognized as a swipe on ordinary content
        MouseSwipe(form, new Point(300, 50), new Point(40, 50));

        // but whoever is under it took the pointer: there is no gesture fight anymore
        Assert.Equal(0, swipes);
    }

    private static (Form Form, DragList List) CreateList()
    {
        var platform = new HeadlessPlatform();

        var list = new DragList
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        list.Items.Add("первая");
        list.Items.Add("вторая");
        list.Items.Add("третья");

        var form = new Form { Size = new Size(400, 300), Content = list };
        platform.CreateWindow(form);
        form.UpdateLayout();

        return (form, list);
    }

    [Fact]
    public void CancelledContactEndsDrag()
    {
        var (form, list) = CreateList();

        form.OnPointerDown(new Point(50, 10));
        form.OnPointerMove(new Point(50, 60));

        Assert.True(list.IsDragging);

        // the platform cut off the contact: there will be no release
        form.OnPointerCancel(Form.MousePointerId);

        // the row must not stay hanging over the form
        Assert.False(list.IsDragging);
    }

    [Fact]
    public void NormalDragCompletes()
    {
        var (form, list) = CreateList();

        form.OnPointerDown(new Point(50, 10));
        form.OnPointerMove(new Point(50, 70));
        form.OnPointerUp(new Point(50, 70));

        // we check not the order — it depends on the row height — but that
        // the move finished and nothing was left hanging
        Assert.False(list.IsDragging);
        Assert.Equal(3, list.Items.Count);
    }
}