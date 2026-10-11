using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.DataGrid;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Mouse;
using ZeppelinForms.Input.Pointer;

namespace ZeppelinForms.UnitTests;

/// <summary>A press held on a scroll bar's track keeps paging, as on every desktop:
/// one page at once, then more after a delay, towards the pointer, until the thumb is
/// under it or the press ends. The same for a panel's bars, the data grid's and the
/// <see cref="ScrollBar"/> control.</summary>
[Collection("Platform")]
public class ScrollBarRepeatTests
{
    private const float Width = 200;
    private const float Height = 100;

    /// <summary>Longer than the delay and several intervals: a held press has
    /// repeated by now.</summary>
    private const int HoldMs = PointerThresholds.RepeatDelayMs + 8 * PointerThresholds.RepeatIntervalMs;

    private static (Form Form, HeadlessPlatform Platform, StackPanel Panel) CreatePanel()
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, OverflowY = Overflow.Auto };

        for (int i = 0; i < 100; i++)
            panel.Children.Add(new Label { Text = $"Row {i}", Size = new Size(float.NaN, 20) });

        var platform = new HeadlessPlatform();
        var form = new Form { Size = new Size(Width, Height), Content = panel };

        platform.CreateWindow(form);
        form.UpdateLayout();

        return (form, platform, panel);
    }

    /// <summary>The scheduled steps come back through the UI queue: pump it, as the
    /// UI thread would, for a while.</summary>
    private static void Hold(Form form, HeadlessPlatform platform, int ms)
    {
        DateTime until = DateTime.UtcNow.AddMilliseconds(ms);

        while (DateTime.UtcNow < until)
        {
            platform.PumpAll();
            form.UpdateLayout();
            Thread.Sleep(5);
        }

        platform.PumpAll();
        form.UpdateLayout();
    }

    /// <summary>A point on the panel's vertical bar, at a height of the window.</summary>
    private static Point OnBar(float y) => new(Width - 4, y);

    [Fact]
    public void APressOnTheTrackPagesOnceAtOnce()
    {
        var (form, _, panel) = CreatePanel();

        form.OnPointerDown(OnBar(Height - 5));

        Assert.Equal(Height, panel.ScrollY, 0.5f);
    }

    [Fact]
    public void AHeldPressKeepsPaging()
    {
        var (form, platform, panel) = CreatePanel();

        form.OnPointerDown(OnBar(Height - 5));
        Hold(form, platform, HoldMs);

        // more than the one page of the press itself
        Assert.True(panel.ScrollY > 2 * Height, $"ScrollY = {panel.ScrollY}");

        form.OnPointerUp(OnBar(Height - 5));
    }

    [Fact]
    public void ReleasingStopsThePaging()
    {
        var (form, platform, panel) = CreatePanel();

        form.OnPointerDown(OnBar(Height - 5));
        form.OnPointerUp(OnBar(Height - 5));

        Hold(form, platform, HoldMs);

        Assert.Equal(Height, panel.ScrollY, 0.5f);
    }

    [Fact]
    public void PagingStopsOnceTheThumbIsUnderThePointer()
    {
        var (form, platform, panel) = CreatePanel();

        // the middle of the track: the thumb comes to it long before the end
        const float y = Height / 2;

        form.OnPointerDown(OnBar(y));
        Hold(form, platform, HoldMs + 400);

        float max = 100 * 20 - Height;
        Assert.True(panel.ScrollY < max, $"ScrollY = {panel.ScrollY}, the end is {max}");

        // the thumb has come under the pointer: its top is above it, its bottom below
        float length = Math.Max(24f, Height * (Height / (100 * 20f)));
        float top = (Height - length) * (panel.ScrollY / max);
        Assert.InRange(y, top, top + length);

        form.OnPointerUp(OnBar(y));
    }

    [Fact]
    public void PagingWaitsWhileThePointerIsOffTheTrack()
    {
        var (form, platform, panel) = CreatePanel();

        form.OnPointerDown(OnBar(Height - 5));

        // the pointer goes over the content: the press is still the bar's,
        // but there is nothing to page towards
        form.OnPointerMove(new Point(20, Height - 5));
        Hold(form, platform, HoldMs);

        Assert.Equal(Height, panel.ScrollY, 0.5f);

        // back on the track — it pages on
        form.OnPointerMove(OnBar(Height - 5));
        Hold(form, platform, HoldMs);

        Assert.True(panel.ScrollY > Height, $"ScrollY = {panel.ScrollY}");

        form.OnPointerUp(OnBar(Height - 5));
    }

    [Fact]
    public void ThePressDoesNotTurnBack()
    {
        var (form, platform, panel) = CreatePanel();
        panel.ScrollTo(0, 1000);
        form.UpdateLayout();

        // pressed below the thumb: it pages down only, even if the pointer goes above
        form.OnPointerDown(OnBar(Height - 5));
        float afterPress = panel.ScrollY;

        form.OnPointerMove(OnBar(2));
        Hold(form, platform, HoldMs);

        Assert.True(panel.ScrollY >= afterPress, $"ScrollY = {panel.ScrollY}, after the press {afterPress}");

        form.OnPointerUp(OnBar(2));
    }

    [Fact]
    public void TheThumbIsStillDragged()
    {
        var (form, _, panel) = CreatePanel();

        // the thumb is at the top: grab it there and pull it to the bottom
        form.OnPointerDown(OnBar(4));
        form.OnPointerMove(OnBar(Height + 50));
        form.OnPointerUp(OnBar(Height + 50));

        Assert.Equal(100 * 20 - Height, panel.ScrollY, 0.5f);
    }

    [Fact]
    public void TheRightButtonDoesNotPage()
    {
        var (form, _, panel) = CreatePanel();

        form.OnPointerDown(OnBar(Height - 5), MouseButton.Right);
        form.OnPointerUp(OnBar(Height - 5), MouseButton.Right);

        Assert.Equal(0, panel.ScrollY, 0.5f);
    }

    [Fact]
    public void TheDataGridTrackRepeatsToo()
    {
        var grid = new DataGridView
        {
            Columns = { new DataGridViewColumn { Header = "N", Value = item => item } },
        };

        for (int i = 0; i < 200; i++)
            grid.Items.Add(i);

        var platform = new HeadlessPlatform();
        var form = new Form { Size = new Size(300, 200), Content = grid };

        platform.CreateWindow(form);
        form.UpdateLayout();

        var point = new Point(300 - 4, 200 - 20);

        form.OnPointerDown(point);
        float afterPress = ScrollOf(grid);

        Hold(form, platform, HoldMs);

        Assert.True(afterPress > 0, "the press paged");
        Assert.True(ScrollOf(grid) > 2 * afterPress, $"held: {ScrollOf(grid)}, after the press {afterPress}");

        form.OnPointerUp(point);

        // the press on the bar is not a click on a row
        Assert.Equal(-1, grid.SelectedIndex);
    }

    [Fact]
    public void TheScrollBarControlRepeatsAlongItsOrientation()
    {
        // set in an initializer — after the constructor: the bar must page along it
        var bar = new ScrollBar
        {
            Orientation = Orientation.Horizontal,
            Size = new Size(200, 12),
            ContentSize = 2000,
            ViewportSize = 100,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        var platform = new HeadlessPlatform();
        var form = new Form { Size = new Size(300, 100), Content = bar };

        platform.CreateWindow(form);
        form.UpdateLayout();

        Point at = bar.GetAbsolutePosition();
        var point = new Point(at.X + 195, at.Y + 6);

        form.OnPointerDown(point);
        Assert.Equal(100, bar.Value, 0.5f);

        Hold(form, platform, HoldMs);
        Assert.True(bar.Value > 200, $"Value = {bar.Value}");

        form.OnPointerUp(point);
    }

    private static float ScrollOf(DataGridView grid) =>
        (float)typeof(DataGridView)
            .GetField("_scrollY", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(grid)!;
}