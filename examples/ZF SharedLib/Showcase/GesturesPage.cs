using System.Globalization;
using ZeppelinForms.Core.Collections;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Gestures;

namespace ZF_SharedLib.Showcase;

/// <summary>Gestures: tap, long press, swipe, pan, pinch and rotate, each with its
/// own recognizer added to an element.</summary>
internal static class GesturesPage
{
    public static UIElement Create() =>
        Demo.Page("Gestures", "Recognizers turn pointer and touch input into taps, swipes, drags, pinches and turns.",
            Demo.Section("Tap, long press, swipe", Simple(), "A mouse works as one finger: click, hold, or flick quickly"),
            Demo.Section("Pan", Pan(), "Drag the card; on release it stays inside its stage"),
            Demo.Section("Pinch and rotate", PinchRotate(), "Two fingers on a touch screen: spread to zoom, turn to rotate; double tap resets"));

    private static UIElement Simple()
    {
        Label log = Demo.Note("Try the pad");

        Label pad = Pad("Tap, hold or swipe");

        pad.AddGesture(new TapGestureRecognizer()).Tapped += (_, _) => log.Text = "Tapped";
        pad.AddGesture(new LongPressGestureRecognizer()).Triggered += (_, e) =>
            log.Text = $"Long press at {e.Location.X:0}, {e.Location.Y:0}";
        pad.AddGesture(new SwipeGestureRecognizer()).Swiped += (_, e) =>
            log.Text = $"Swiped {e.Direction.ToString().ToLowerInvariant()} at {e.Velocity.ToString("0", CultureInfo.CurrentCulture)} px/s";

        var root = Stretch();
        root.Children.AddRange([pad, log]);
        return root;
    }

    private static UIElement Pan()
    {
        Label log = Demo.Note("Drag the card");

        var card = new Label
        {
            Text = "Drag me",
            Size = new(120, 72),
            CornerRadius = new(8),
            Background = new Color(255, 0x66, 0x10, 0xF2),
            TextColor = Colors.White,
            HorizontalContentAlign = HorizontalContentAlignment.Center,
            VerticalContentAlign = VerticalContentAlignment.Center,
            Position = new(20, 40),
        };

        var stage = new Panel { Size = new(float.NaN, 200), Background = new Color(20, 128, 128, 128), CornerRadius = new(8) };
        stage.Children.Add(card);

        Point start = card.Position;
        PanGestureRecognizer pan = card.AddGesture(new PanGestureRecognizer());

        pan.Started += (_, _) => start = card.Position;
        pan.Updated += (_, e) =>
        {
            card.Position = new(start.X + e.TotalOffset.X, start.Y + e.TotalOffset.Y);
            log.Text = $"Moved by {e.TotalOffset.X:0}, {e.TotalOffset.Y:0}";
        };
        pan.Completed += (_, e) =>
        {
            log.Text = $"Released at {Math.Abs(e.Velocity.X) + Math.Abs(e.Velocity.Y):0} px/s";

            // keep it inside the stage
            float maxX = Math.Max(0, stage.ActualSize.Width - card.ActualSize.Width);
            float maxY = Math.Max(0, stage.ActualSize.Height - card.ActualSize.Height);
            card.Position = new(Math.Clamp(card.Position.X, 0, maxX), Math.Clamp(card.Position.Y, 0, maxY));
        };

        var root = Stretch();
        root.Children.AddRange([stage, log]);
        return root;
    }

    private static UIElement PinchRotate()
    {
        Label log = Demo.Note("Scale 100%, angle 0°");

        var photo = new Label
        {
            Text = "Pinch me",
            Size = new(160, 110),
            CornerRadius = new(8),
            Background = new Color(255, 0xFD, 0x7E, 0x14),
            TextColor = Colors.White,
            HorizontalContentAlign = HorizontalContentAlignment.Center,
            VerticalContentAlign = VerticalContentAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        float scale = 1f, angle = 0f, startScale = 1f, startAngle = 0f;

        void Show()
        {
            photo.ScaleX = photo.ScaleY = scale;
            photo.Rotation = angle;
            log.Text = $"Scale {scale.ToString("P0", CultureInfo.CurrentCulture)}, angle {angle:0}°";
        }

        PinchGestureRecognizer pinch = photo.AddGesture(new PinchGestureRecognizer());
        pinch.Started += (_, _) => startScale = scale;
        pinch.Updated += (_, e) => { scale = Math.Clamp(startScale * e.Scale, 0.4f, 3f); Show(); };

        RotateGestureRecognizer rotate = photo.AddGesture(new RotateGestureRecognizer());
        rotate.Started += (_, _) => startAngle = angle;
        rotate.Updated += (_, e) => { angle = startAngle + e.Angle; Show(); };

        photo.DoubleClick += (_, _) => { scale = 1f; angle = 0f; Show(); };

        var stage = new Border
        {
            Size = new(float.NaN, 260),
            Child = photo,
        };

        var root = Stretch();
        root.Children.AddRange([stage, log]);
        return root;
    }

    private static Label Pad(string text) => new()
    {
        Text = text,
        Size = new(float.NaN, 140),
        CornerRadius = new(8),
        BorderWidth = 1,
        BorderColor = new Color(120, 128, 128, 128),
        Background = new Color(20, 128, 128, 128),
        HorizontalContentAlign = HorizontalContentAlignment.Center,
        VerticalContentAlign = VerticalContentAlignment.Center,
    };

    private static StackPanel Stretch() => new()
    {
        Orientation = Orientation.Vertical,
        Spacing = 8,
        CrossAxisAlignment = CrossAxisAlignment.Stretch,
    };
}
