using Xunit;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Gestures;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.UnitTests;

[Collection("Platform")]
public class TouchScrollTests
{
    private sealed class Row : UnitControl
    {
        public override void Draw(Graphics g) { }

        protected override Size MeasureOverride(Size availableSize) => new(100, 40);
    }

    private static StackPanel CreateScroller(int rows = 50)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            OverflowY = Overflow.Auto,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        for (int i = 0; i < rows; i++)
            panel.Children.Add(new Row());

        return panel;
    }

    private static Form Show(UIElement content)
    {
        var platform = new HeadlessPlatform();

        var form = new Form { Size = new Size(400, 300), Content = content };
        platform.CreateWindow(form);
        form.UpdateLayout();

        return form;
    }

    /// <summary>Медленное протаскивание: палец замирает перед отпусканием,
    /// броска нет, и инерция не начинается.</summary>
    private static void SlowDrag(Form form, Point from, Point to, int steps = 10)
    {
        HeadlessInput.TouchDown(form, 0, from.X, from.Y, 0);

        for (int i = 1; i <= steps; i++)
        {
            float t = (float)i / steps;

            HeadlessInput.TouchMove(
                form, 0,
                from.X + ((to.X - from.X) * t),
                from.Y + ((to.Y - from.Y) * t),
                i * 16);
        }

        HeadlessInput.TouchUp(form, 0, to.X, to.Y, steps * 16 + 200);
    }

    [Fact]
    public void TouchDragScrolls()
    {
        StackPanel panel = CreateScroller();
        Form form = Show(panel);

        SlowDrag(form, new Point(100, 250), new Point(100, 50));
        form.UpdateLayout();

        // порог срыва — не потерянное расстояние: уехали на весь путь пальца
        Assert.InRange(panel.ScrollY, 190f, 210f);
    }

    [Fact]
    public void FlingContinuesAfterRelease()
    {
        StackPanel panel = CreateScroller();
        Form form = Show(panel);

        HeadlessInput.Swipe(form, new Point(100, 250), new Point(100, 50));
        form.UpdateLayout();

        float released = panel.ScrollY;

        form.Clock.Advance(TimeSpan.FromMilliseconds(300));
        form.UpdateLayout();

        // бросок: после отпускания содержимое продолжает ехать
        Assert.True(panel.ScrollY > released + 50f);
    }

    [Fact]
    public void TouchDuringFlingStopsIt()
    {
        StackPanel panel = CreateScroller();
        Form form = Show(panel);

        HeadlessInput.Swipe(form, new Point(100, 250), new Point(100, 50));
        form.Clock.Advance(TimeSpan.FromMilliseconds(50));

        // палец лёг на экран — инерция должна замереть
        HeadlessInput.TouchDown(form, 0, 100, 150, 1000);
        form.UpdateLayout();

        float stopped = panel.ScrollY;

        form.Clock.Advance(TimeSpan.FromMilliseconds(300));
        form.UpdateLayout();

        Assert.Equal(stopped, panel.ScrollY);

        HeadlessInput.TouchUp(form, 0, 100, 150, 1300);
    }

    [Fact]
    public void MouseDragDoesNotScroll()
    {
        StackPanel panel = CreateScroller();
        Form form = Show(panel);

        form.OnPointerDown(new Point(100, 250));

        for (int i = 1; i <= 10; i++)
            form.OnPointerMove(new Point(100, 250 - i * 20));

        form.OnPointerUp(new Point(100, 50));
        form.UpdateLayout();

        // мышью на десктопе протаскивают выделение и строки, а не содержимое
        Assert.Equal(0f, panel.ScrollY);
    }

    [Fact]
    public void PullBeyondTopSpringsBack()
    {
        StackPanel panel = CreateScroller();
        Form form = Show(panel);

        UIElement first = panel.Children[0];

        // тянем вниз от верхнего края — дальше прокручивать некуда
        SlowDrag(form, new Point(100, 50), new Point(100, 200));
        form.UpdateLayout();

        Assert.True(first.Position.Y > 0f, "оттянутое содержимое должно уехать за край");
        Assert.Equal(0f, panel.ScrollY);

        form.Clock.Advance(TimeSpan.FromSeconds(1));
        form.UpdateLayout();

        // пружина вернула его на место
        Assert.Equal(0f, first.Position.Y);
    }

    [Fact]
    public void HorizontalSwipeStillReachesOuterRecognizer()
    {
        StackPanel scroller = CreateScroller();

        var outer = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        scroller.Size = new Size(float.NaN, 280);
        outer.Children.Add(scroller);

        var swipe = new SwipeGestureRecognizer();
        int swipes = 0;

        swipe.Swiped += (_, _) => swipes++;
        outer.AddGesture(swipe);

        Form form = Show(outer);

        HeadlessInput.Swipe(form, new Point(350, 150), new Point(40, 150));
        form.UpdateLayout();

        // вертикальный список не забирает горизонтальный жест
        Assert.Equal(1, swipes);
        Assert.Equal(0f, scroller.ScrollY);
    }

    [Fact]
    public void ShortInnerPanelLeavesGestureToOuter()
    {
        StackPanel outer = CreateScroller(rows: 40);

        // внутренняя прокручиваемая панель, которой прокручивать нечего
        var inner = new StackPanel
        {
            Orientation = Orientation.Vertical,
            OverflowY = Overflow.Auto,
        };

        inner.Children.Add(new Row());
        outer.Children.Insert(0, inner);

        Form form = Show(outer);

        // палец ложится на короткую панель и тянет вверх
        SlowDrag(form, new Point(50, 20), new Point(50, -180));
        form.UpdateLayout();

        // жест, начатый на короткой панели, достался внешней
        Assert.True(outer.ScrollY > 0f);
    }

    [Fact]
    public void VelocityTrackerMeasuresRecentMotion()
    {
        var tracker = new VelocityTracker();

        // долго тянули медленно, в конце бросили быстро
        for (int i = 0; i <= 20; i++)
            tracker.Add(new Point(0, i), i * 16);

        tracker.Add(new Point(0, 120), 21 * 16);
        tracker.Add(new Point(0, 220), 22 * 16);

        Point velocity = tracker.GetVelocity(22 * 16);

        // скорость — по последним ста миллисекундам, а не по всему жесту
        Assert.True(velocity.Y > 1000f);
    }

    [Fact]
    public void VelocityTrackerIgnoresPauseBeforeRelease()
    {
        var tracker = new VelocityTracker();

        for (int i = 0; i <= 5; i++)
            tracker.Add(new Point(0, i * 30), i * 16);

        // палец замер на 200 мс и только потом ушёл — броска не было
        Assert.Equal(Point.Empty, tracker.GetVelocity(5 * 16 + 200));
    }
}