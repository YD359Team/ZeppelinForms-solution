using Xunit;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

[Collection("Platform")]
public class RenderCullingTests
{
    /// <summary>Контрол, который только считает, сколько раз его рисовали.</summary>
    private sealed class CountingBox : UnitControl
    {
        public int Draws { get; private set; }

        public override void Draw(Graphics g) => Draws++;

        protected override Size MeasureOverride(Size availableSize) => new(100, 20);
    }

    [Fact]
    public void ChildrenOutsideViewportAreNotDrawn()
    {
        var platform = new HeadlessPlatform();

        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            OverflowY = Overflow.Auto,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        CountingBox[] boxes = [.. Enumerable.Range(0, 100).Select(_ => new CountingBox())];

        foreach (CountingBox box in boxes)
            panel.Children.Add(box);

        // 300 / 20 = пятнадцать строк в окне, остальные восемьдесят пять за краем
        var form = new Form { Size = new Size(400, 300), Content = panel };
        platform.CreateWindow(form);
        form.UpdateLayout();

        // clip не задан: так рисуют Android и браузер — там частичной
        // перерисовки нет вообще, и раньше отсечения тоже не было
        ElementTreeRenderer.Draw(panel, new HeadlessGraphics());

        int drawn = boxes.Count(box => box.Draws > 0);

        Assert.InRange(drawn, 1, 20);
        Assert.True(boxes[0].Draws > 0, "первая строка видна и должна рисоваться");
        Assert.Equal(0, boxes[^1].Draws);
    }

    [Fact]
    public void ScrolledPanelDrawsRowsAtNewOffset()
    {
        var platform = new HeadlessPlatform();

        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            OverflowY = Overflow.Auto,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        CountingBox[] boxes = [.. Enumerable.Range(0, 100).Select(_ => new CountingBox())];

        foreach (CountingBox box in boxes)
            panel.Children.Add(box);

        var form = new Form { Size = new Size(400, 300), Content = panel };
        platform.CreateWindow(form);

        panel.ScrollTo(0, 1000);   // 1000 / 20 = пятидесятая строка наверху
        form.UpdateLayout();

        ElementTreeRenderer.Draw(panel, new HeadlessGraphics());

        Assert.Equal(0, boxes[0].Draws);
        Assert.True(boxes[50].Draws > 0, "строка под новой прокруткой должна рисоваться");
    }

    [Fact]
    public void RotationDisablesCullingInSubtree()
    {
        var platform = new HeadlessPlatform();

        var inner = new StackPanel
        {
            Orientation = Orientation.Vertical,
            OverflowY = Overflow.Auto,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Rotation = 30f,
        };

        CountingBox[] boxes = [.. Enumerable.Range(0, 40).Select(_ => new CountingBox())];

        foreach (CountingBox box in boxes)
            inner.Children.Add(box);

        var form = new Form { Size = new Size(400, 300), Content = inner };
        platform.CreateWindow(form);
        form.UpdateLayout();

        ElementTreeRenderer.Draw(inner, new HeadlessGraphics());

        // под поворотом сложение смещений не работает, поэтому отсечения
        // нет: рисуется всё поддерево, пусть и с запасом
        Assert.All(boxes, box => Assert.True(box.Draws > 0));
    }
}