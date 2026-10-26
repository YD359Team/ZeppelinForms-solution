using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>A native control among the form's elements: made when its host is first in a
/// window that can hold it, following the host — its place, a scroll, its visibility —
/// and gone with the host or the window.</summary>
[Collection("Platform")]
public sealed class NativeControlHostTests
{
    private sealed class TestHost : NativeControlHost
    {
        public PlatformHandle? PlatformParent { get; private set; }
        public int Created { get; private set; }
        public List<PlatformHandle> Destroyed { get; } = [];

        protected override PlatformHandle CreateNativeControlCore(PlatformHandle parent)
        {
            PlatformParent = parent;
            Created++;
            return new PlatformHandle(1000 + Created, "Test");
        }

        protected override void DestroyNativeControlCore(PlatformHandle control) => Destroyed.Add(control);
    }

    private static (Form Form, HeadlessWindow Window) Host(UIElement content)
    {
        var form = new Form { Size = new Size(400, 300), Content = content };
        var window = (HeadlessWindow)new HeadlessPlatform().CreateWindow(form);
        form.Show();
        form.UpdateLayout();
        form.PlaceNativeControls();

        return (form, window);
    }

    private static StackPanel Column(params UIElement[] children)
    {
        var panel = new StackPanel { Padding = new Thickness(10), Spacing = 5 };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }

    [Fact]
    public void TheControlIsMadeInTheSlotAndPlacedWhereTheHostIs()
    {
        var host = new TestHost { Size = new Size(200, 100), HorizontalAlignment = HorizontalAlignment.Left };
        int created = 0;
        host.NativeControlCreated += (_, _) => created++;

        (_, HeadlessWindow window) = Host(Column(new Button { Text = "above" }, host));

        HeadlessNativeSlot slot = Assert.Single(window.NativeSlots);
        Assert.Equal(slot.Parent, host.PlatformParent);
        Assert.Equal(new PlatformHandle(1001, "Test"), slot.Control);
        Assert.Equal(slot.Control, host.NativeControl);
        Assert.Equal(1, created);

        Point at = host.GetAbsolutePosition();
        Assert.Equal(new Rectangle(at, new Size(200, 100)), slot.Bounds);
        Assert.Equal(slot.Bounds, slot.Visible);
    }

    [Fact]
    public void AScrolledHostIsCutAtTheViewport()
    {
        var host = new TestHost { Size = new Size(200, 300), HorizontalAlignment = HorizontalAlignment.Left };
        var list = new StackPanel { Size = new Size(260, 120), OverflowY = Overflow.Auto, Children = { host } };

        (Form form, HeadlessWindow window) = Host(Column(list));
        HeadlessNativeSlot slot = window.NativeSlots[0];

        Point listAt = list.GetAbsolutePosition();
        Assert.Equal(listAt.Y, slot.Visible.Y, 0.5f);
        Assert.True(slot.Visible.Height <= 120.5f, "cut at the viewport's bottom");

        list.ScrollTo(0, 50);
        form.UpdateLayout();
        form.PlaceNativeControls();

        // the control moves up with the content; the visible part stays the viewport's
        Assert.Equal(listAt.Y - 50, slot.Bounds.Y, 0.5f);
        Assert.Equal(listAt.Y, slot.Visible.Y, 0.5f);
    }

    [Fact]
    public void AHiddenHostHidesItsControl()
    {
        var host = new TestHost { Size = new Size(100, 50) };
        (Form form, HeadlessWindow window) = Host(Column(host));
        HeadlessNativeSlot slot = window.NativeSlots[0];

        host.IsVisible = false;
        form.UpdateLayout();
        form.PlaceNativeControls();

        Assert.Equal(Rectangle.Empty, slot.Visible);
        Assert.NotNull(host.NativeControl);

        host.IsVisible = true;
        form.UpdateLayout();
        form.PlaceNativeControls();

        Assert.NotEqual(Rectangle.Empty, slot.Visible);
        Assert.Single(window.NativeSlots);
    }

    [Fact]
    public void AHostLeavingTheFormDestroysItsControl()
    {
        var host = new TestHost { Size = new Size(100, 50) };
        StackPanel column = Column(host);
        (_, HeadlessWindow window) = Host(column);
        int destroying = 0;
        host.NativeControlDestroying += (_, _) => destroying++;

        column.Children.Remove(host);

        Assert.Equal([new PlatformHandle(1001, "Test")], host.Destroyed);
        Assert.Equal(1, destroying);
        Assert.True(window.NativeSlots[0].IsDisposed);
        Assert.Null(host.NativeControl);
    }

    [Fact]
    public void TheControlGoesWithTheWindowAndComesWithANewOne()
    {
        var host = new TestHost { Size = new Size(100, 50) };
        (Form form, HeadlessWindow first) = Host(Column(host));

        form.Close();

        Assert.Single(host.Destroyed);
        Assert.True(first.NativeSlots[0].IsDisposed);

        var second = (HeadlessWindow)new HeadlessPlatform().CreateWindow(form);
        form.Show();
        form.UpdateLayout();
        form.PlaceNativeControls();

        Assert.Single(second.NativeSlots);
        Assert.Equal(2, host.Created);
    }

    [Fact]
    public void AWindowWithoutNativeControlsGetsThePlaceholder()
    {
        var host = new TestHost { Size = new Size(100, 50) };
        var form = new Form { Size = new Size(400, 300), Content = Column(host) };
        var window = (HeadlessWindow)new HeadlessPlatform().CreateWindow(form);
        window.SupportsNativeControls = false;

        form.Show();
        form.UpdateLayout();
        form.PlaceNativeControls();

        Assert.Null(host.NativeControl);
        Assert.Equal(0, host.Created);
    }

    [Fact]
    public void TheKeyboardGoesToTheControlAndComesFromIt()
    {
        var host = new TestHost { Size = new Size(100, 50) };
        var box = new TextBox();
        (Form form, HeadlessWindow window) = Host(Column(box, host));
        HeadlessNativeSlot slot = window.NativeSlots[0];

        // the form's focus moved to the host: the keyboard goes on to the control
        host.Focus();
        Assert.Equal(1, slot.FocusCount);

        form.FocusElement(box);

        // a click into the control: the form's focus follows, the keyboard isn't asked for again
        slot.SimulateControlFocus();
        Assert.Same(host, form.FocusedElement);
        Assert.Equal(1, slot.FocusCount);
    }
}