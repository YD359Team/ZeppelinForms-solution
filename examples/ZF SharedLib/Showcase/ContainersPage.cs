using System.Globalization;
using ZeppelinForms;
using ZeppelinForms.Core.Collections;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;

namespace ZF_SharedLib.Showcase;

/// <summary>Controls that hold one child and add something around it: a frame,
/// a caption, a fold, zoom, handles, pull to refresh.</summary>
internal static class ContainersPage
{
    public static UIElement Create() =>
        Demo.Page("Containers", "One child each, and something around it.",
            Demo.Section("Borders", Borders(), "Border draws a frame, a fill and round corners; GradientBorder fills with a gradient"),
            Demo.Section("Group box and spoiler", Groups(), "A spoiler folds away by its header: click it, or Space and Enter"),
            Demo.Section("Zoom box", Zoom(), "Ctrl + wheel or a pinch zooms the child; the buttons call Zoom"),
            Demo.Section("Grip box", Grip(), "Drag the handles to resize the child, the round one above to rotate it; Shift snaps the angle"),
            Demo.Section("Pull to refresh", Refresh(), "Pull the list down with the mouse or a finger; the indicator spins until the work is done"));

    private static UIElement Borders()
    {
        var plain = new Border
        {
            BorderWidth = 1,
            BorderColor = new Color(255, 0x0D, 0x6E, 0xFD),
            Padding = new(16, 10),
            Child = new Label { Text = "Square corners" },
        };

        var rounded = new Border
        {
            BorderWidth = 2,
            BorderColor = new Color(255, 0x19, 0x87, 0x54),
            CornerRadius = new(12),
            Padding = new(16, 10),
            Child = new Label { Text = "Round corners" },
        };

        var corners = new Border
        {
            Background = new Color(40, 0xD6, 0x33, 0x84),
            CornerRadius = new(20, 0, 20, 0),
            Padding = new(16, 10),
            Child = new Label { Text = "Each corner its own" },
        };

        var gradient = new GradientBorder
        {
            Angle = 90,
            CornerRadius = new(8),
            Padding = new(16, 10),
            Child = new Label { Text = "Gradient", TextColor = Colors.White },
        };
        gradient.SetStops(new Color(255, 0x66, 0x10, 0xF2), new Color(255, 0xFD, 0x7E, 0x14));

        return Demo.Row(plain, rounded, corners, gradient);
    }

    private static UIElement Groups()
    {
        var group = new GroupBox
        {
            Header = "Delivery",
            Padding = new(12, 20, 12, 12),
            Child = Demo.Column(
                new RadioButton { Text = "Courier", GroupName = "containers-delivery" },
                new RadioButton { Text = "Pick-up point", GroupName = "containers-delivery" }),
        };

        var spoiler = new Spoiler
        {
            Header = "What is inside?",
            IsCollapsed = true,
            Size = new(320, float.NaN),
            Child = new Label
            {
                Text = "A spoiler hides its child\nuntil the header is clicked.\nThe page below moves to make room.",
                Padding = new(12),
            },
        };

        return Demo.Row(group, spoiler);
    }

    private static UIElement Zoom()
    {
        var picture = new PictureBox { Size = new(160, 160), Layout = ImageLayout.Zoom };
        picture.SetImage(Assets.LogoImage(320));

        var box = new ZoomBox
        {
            Size = new(float.NaN, 220),
            MinZoom = 0.5f,
            MaxZoom = 4f,
            Child = picture,
        };

        Label factor = Demo.Note("100%");

        void Show() => factor.Text = box.ZoomFactor.ToString("P0", CultureInfo.CurrentCulture);

        var zoomIn = new Button { Text = "+" };
        zoomIn.Click += (_, _) => { box.Zoom(box.ZoomFactor * 1.25f); Show(); };

        var zoomOut = new Button { Text = "−" };
        zoomOut.Click += (_, _) => { box.Zoom(box.ZoomFactor / 1.25f); Show(); };

        var reset = new Button { Text = "100%" };
        reset.Click += (_, _) => { box.Zoom(1f); Show(); };

        box.MouseWheel += (_, _) => Show();

        var root = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10, CrossAxisAlignment = CrossAxisAlignment.Stretch };
        root.Children.AddRange([Demo.Row(zoomOut, zoomIn, reset, factor), box]);
        return root;
    }

    private static UIElement Grip()
    {
        Label readout = Demo.Note("Grab a handle");

        var sticker = new Label
        {
            Text = "Resize me",
            Size = new(140, 70),
            Background = new Color(255, 0xFD, 0x7E, 0x14),
            TextColor = Colors.White,
            CornerRadius = new(6),
            HorizontalContentAlign = HorizontalContentAlignment.Center,
            VerticalContentAlign = VerticalContentAlignment.Center,
        };

        var grip = new GripBox
        {
            AllowResize = true,
            AllowRotate = true,
            RotationSnap = 15,
            MinChildSize = new(60, 40),
            Child = sticker,
            Position = new(60, 50),
        };

        grip.ChildResized += (_, _) => readout.Text = $"Size: {sticker.Size.Width:0} × {sticker.Size.Height:0}";
        grip.ChildRotated += (_, _) => readout.Text = $"Angle: {sticker.Rotation:0}°";

        // a free panel: the grip box stays where it is while its child grows
        var stage = new Panel { Size = new(float.NaN, 240) };
        stage.Children.Add(grip);

        var root = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10, CrossAxisAlignment = CrossAxisAlignment.Stretch };
        root.Children.AddRange([stage, readout]);
        return root;
    }

    private static UIElement Refresh()
    {
        int round = 0;

        var news = new ListBox { Size = new(float.NaN, 200), OverflowY = Overflow.Auto };
        news.Items.AddRange(["Pull down to load more", "Older news 2", "Older news 1"]);

        var refresh = new RefreshContainer { AllowMousePull = true, Child = news };

        refresh.RefreshRequested += async (_, e) =>
        {
            // the deferral keeps the indicator spinning until the "network" answers
            using var deferral = e.GetDeferral();

            await Task.Delay(1200);
            news.Items.Insert(0, $"Fresh news {++round}");
        };

        return refresh;
    }
}
