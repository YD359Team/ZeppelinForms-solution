using Xunit;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.UnitTests;

/// <summary>ZoomBox zooms around a point — the cursor, the fingers, its middle —
/// and keeps the zoomed child in sight. It used to scale around its top-left corner,
/// and a child in the middle slid out of view as it grew.</summary>
[Collection("Platform")]
public class ZoomBoxTests
{
    private sealed class Picture : UnitControl
    {
        public override void Draw(Graphics g) { }

        protected override Size MeasureOverride(Size availableSize) => ResolveSize(new Size(100, 100), availableSize);
    }

    private static (Form Form, ZoomBox Box, Picture Child) Show(KeyModifiers wheel = KeyModifiers.None)
    {
        var child = new Picture();
        var box = new ZoomBox { MinZoom = 0.5f, MaxZoom = 4f, WheelZoomModifiers = wheel, Child = child };

        var form = new Form { Size = new Size(400, 300), Content = box };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return (form, box, child);
    }

    /// <summary>The child's point drawn at a point of the box.</summary>
    private static Point Under(ZoomBox box, Point point) => box.TransformPointToChild(point);

    private static Point Middle(UIElement e) =>
        new(e.Position.X + e.ActualSize.Width / 2f, e.Position.Y + e.ActualSize.Height / 2f);

    [Fact]
    public void ZoomKeepsTheMiddleInTheMiddle()
    {
        var (_, box, child) = Show();
        Rectangle view = box.ContentBounds;
        var middle = new Point(view.X + view.Width / 2f, view.Y + view.Height / 2f);

        box.Zoom(2f);

        // the child stands in the middle of the box, and stays there
        Point under = Under(box, middle);
        Assert.Equal(Middle(child).X, under.X, 0.5f);
        Assert.Equal(Middle(child).Y, under.Y, 0.5f);
    }

    [Fact]
    public void ZoomKeepsThePointUnderTheAnchor()
    {
        var (_, box, child) = Show();
        var anchor = new Point(child.Position.X + 20, child.Position.Y + 30);

        // still smaller than the box: nothing has to be pulled back into sight
        box.Zoom(2f, anchor);

        Point under = Under(box, anchor);
        Assert.Equal(anchor.X, under.X, 0.5f);
        Assert.Equal(anchor.Y, under.Y, 0.5f);
    }

    [Fact]
    public void TheZoomedChildCoversTheBox()
    {
        var (_, box, child) = Show();

        // around the corner, as it used to be: the child would have left the box
        box.Zoom(4f, new Point(0, 0));

        Rectangle view = box.ContentBounds;
        Point topLeft = Under(box, new Point(view.X, view.Y));
        Point bottomRight = Under(box, new Point(view.Right, view.Bottom));

        // 400 by 400 now: on both axes it covers the box, no empty strip at an edge
        Assert.InRange(topLeft.X, child.Position.X - 0.5f, child.Position.X + child.ActualSize.Width);
        Assert.InRange(bottomRight.X, child.Position.X, child.Position.X + child.ActualSize.Width + 0.5f);
        Assert.InRange(topLeft.Y, child.Position.Y - 0.5f, child.Position.Y + child.ActualSize.Height);
        Assert.InRange(bottomRight.Y, child.Position.Y, child.Position.Y + child.ActualSize.Height + 0.5f);
    }

    [Fact]
    public void ResetPutsTheChildBack()
    {
        var (_, box, _) = Show();

        box.Zoom(3f, new Point(10, 10));
        box.ResetZoom();

        Assert.Equal(1f, box.ZoomFactor);
        Assert.Equal(Point.Empty, box.PanOffset);
    }

    [Fact]
    public void TheWheelZoomsAroundTheCursor()
    {
        var (form, box, child) = Show();
        var cursor = new Point(child.Position.X + 10, child.Position.Y + 10);
        int changes = 0;
        box.ZoomChanged += (_, _) => changes++;

        form.OnMouseWheel(cursor, 120);

        Assert.True(box.ZoomFactor > 1f);
        Assert.Equal(1, changes);

        Point under = Under(box, cursor);
        Assert.Equal(cursor.X, under.X, 0.5f);
        Assert.Equal(cursor.Y, under.Y, 0.5f);
    }

    [Fact]
    public void WithModifiersThePlainWheelIsLeftToThePage()
    {
        var (form, box, child) = Show(KeyModifiers.Control);

        form.OnMouseWheel(Middle(child), 120);

        Assert.Equal(1f, box.ZoomFactor);
    }

    [Fact]
    public void HitTestingFollowsTheZoom()
    {
        var (form, box, child) = Show();

        // zoomed around its middle, the child grows over the box's corner
        box.Zoom(4f);

        Assert.Same(child, ZeppelinForms.Forms.Controls.Tools.HitTester.HitTest(form.Content!, new Point(5, 5)));
    }
}
