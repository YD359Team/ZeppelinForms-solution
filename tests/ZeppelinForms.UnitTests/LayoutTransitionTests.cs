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
public class LayoutTransitionTests
{
    private sealed class ProbeBox : UnitControl
    {
        public float SeenTranslateY { get; private set; }

        public override void Draw(Graphics g) => SeenTranslateY = TranslateY;

        protected override Size MeasureOverride(Size availableSize) => new(100, 40);
    }

    private static (Form Form, StackPanel Panel, ProbeBox Second) CreateForm()
    {
        var platform = new HeadlessPlatform();

        var first = new ProbeBox();
        var second = new ProbeBox();

        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ChildrenLayoutTransition = LayoutTransition.Ease(200, Easing.Linear),
        };

        panel.Children.Add(first);
        panel.Children.Add(second);

        var form = new Form { Size = new Size(400, 300), Content = panel };
        platform.CreateWindow(form);
        form.UpdateLayout();

        return (form, panel, second);
    }

    private static void Render(Form form) =>
        ElementTreeRenderer.Draw(form.Content!, new HeadlessGraphics());

    [Fact]
    public void InsertedNeighbourMakesRowTravelFromOldPlace()
    {
        var (form, panel, second) = CreateForm();

        float before = second.Position.Y;

        panel.Children.Insert(0, new ProbeBox());
        form.UpdateLayout();

        // раскладка уже поставила строку на новое место
        Assert.Equal(before + 40f, second.Position.Y);

        // а нарисована она пока от старого
        Render(form);
        Assert.InRange(second.SeenTranslateY, -41f, -39f);
    }

    [Fact]
    public void TravelDecaysToZero()
    {
        var (form, panel, second) = CreateForm();

        panel.Children.Insert(0, new ProbeBox());
        form.UpdateLayout();

        form.Clock.Advance(TimeSpan.FromMilliseconds(100));
        Render(form);

        Assert.InRange(second.SeenTranslateY, -25f, -15f);

        form.Clock.Advance(TimeSpan.FromMilliseconds(100));
        Render(form);

        Assert.Equal(0f, second.SeenTranslateY);
    }

    [Fact]
    public void LayoutIsUntouchedByTravel()
    {
        var (form, panel, second) = CreateForm();

        panel.Children.Insert(0, new ProbeBox());
        form.UpdateLayout();

        float placed = second.Position.Y;

        form.Clock.Advance(TimeSpan.FromMilliseconds(100));
        form.UpdateLayout();

        // переезд рисуется сдвигом: место из раскладки не меняется,
        // соседи не разъезжаются, размеры не пересчитываются
        Assert.Equal(placed, second.Position.Y);
        Assert.Equal(0f, second.TranslateY);
    }

    [Fact]
    public void WithoutRuleMoveIsInstant()
    {
        var (form, panel, second) = CreateForm();

        panel.ChildrenLayoutTransition = null;

        panel.Children.Insert(0, new ProbeBox());
        form.UpdateLayout();

        Render(form);

        Assert.Equal(0f, second.SeenTranslateY);
    }
}