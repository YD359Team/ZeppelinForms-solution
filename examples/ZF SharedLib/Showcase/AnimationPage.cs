using ZeppelinForms.Animation;
using ZeppelinForms.Core.Collections;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;

namespace ZF_SharedLib.Showcase;

/// <summary>Motion: easing curves, property transitions, animated layout, elements
/// that come and go, endless loops.</summary>
internal static class AnimationPage
{
    private static readonly Color[] Palette =
    [
        new(255, 0x0D, 0x6E, 0xFD),
        new(255, 0x66, 0x10, 0xF2),
        new(255, 0xD6, 0x33, 0x84),
        new(255, 0xFD, 0x7E, 0x14),
        new(255, 0x19, 0x87, 0x54),
    ];

    public static UIElement Create() =>
        Demo.Page("Animation", "Values that travel instead of jumping. With reduced motion in Settings, they jump.",
            Demo.Section("Easing", Easings(), "element.Animate(key, from, to, duration, interpolate, apply, easing)"),
            Demo.Section("Property transitions", Transitions(), "element.Transitions.Add(Transition.Ease(property, ms)): every assignment then travels"),
            Demo.Section("Layout transitions", Layout(), "ChildrenLayoutTransition moves the children to their new places; Enter and Exit transitions bring them in and out"),
            Demo.Section("Show and hide", Visibility(), "EnterTransition and ExitTransition play when IsVisible changes"),
            Demo.Section("Loops", Loops(), "element.AnimateLoop(key, period, apply) until element.StopAnimation(key)"));

    private static UIElement Easings()
    {
        (string Name, Func<float, float> Curve)[] curves =
        [
            ("Linear", Easing.Linear),
            ("EaseIn", Easing.EaseIn),
            ("EaseOut", Easing.EaseOut),
            ("EaseInOut", Easing.EaseInOut),
        ];

        const float BallSize = 28f;

        var track = new Panel { Size = new(float.NaN, curves.Length * 40f) };
        var balls = new List<Label>();

        for (int i = 0; i < curves.Length; i++)
        {
            var ball = new Label
            {
                Text = curves[i].Name,
                Padding = new(10, 0),
                Size = new(float.NaN, BallSize),
                CornerRadius = new(BallSize / 2),
                Background = Palette[i],
                TextColor = Colors.White,
                VerticalContentAlign = VerticalContentAlignment.Center,
                Position = new(0, i * 40f + 6),
            };

            balls.Add(ball);
            track.Children.Add(ball);
        }

        bool there = false;

        var run = new PrimaryButton { Text = "Run" };
        run.Click += (_, _) =>
        {
            there = !there;

            for (int i = 0; i < balls.Count; i++)
            {
                Label ball = balls[i];
                float y = ball.Position.Y;
                float end = Math.Max(0, track.ActualSize.Width - ball.ActualSize.Width);

                ball.Animate(
                    "race",
                    ball.Position.X,
                    there ? end : 0f,
                    TimeSpan.FromMilliseconds(1200),
                    Interpolators.Float,
                    x => ball.Position = new(x, y),
                    curves[i].Curve);
            }
        };

        var root = Stretch();
        root.Children.AddRange([Demo.Row(run), track]);
        return root;
    }

    private static UIElement Transitions()
    {
        var box = new Label
        {
            Text = "Click the buttons",
            Size = new(220, 90),
            Background = Palette[0],
            TextColor = Colors.White,
            CornerRadius = new(4),
            HorizontalContentAlign = HorizontalContentAlignment.Center,
            VerticalContentAlign = VerticalContentAlignment.Center,
        };

        // the rules: these properties travel to every new value
        box.Transitions.Add(Transition.Ease(UIElement.BackgroundProperty, 400));
        box.Transitions.Add(Transition.Ease(UIElement.CornerRadiusProperty, 400));
        box.Transitions.Add(Transition.Ease(UIElement.OpacityProperty, 400));

        int color = 0;

        var recolor = new Button { Text = "Color" };
        recolor.Click += (_, _) => box.Background = Palette[++color % Palette.Length];

        var round = new Button { Text = "Corners" };
        round.Click += (_, _) => box.CornerRadius = box.CornerRadius.TopLeft > 10 ? new(4) : new(45);

        var fade = new Button { Text = "Opacity" };
        fade.Click += (_, _) => box.Opacity = box.Opacity > 0.5f ? 0.25f : 1f;

        return Demo.Column(Demo.Row(recolor, round, fade), box);
    }

    private static UIElement Layout()
    {
        var wrap = new WrapPanel
        {
            Spacing = 8,
            LineSpacing = 8,
            ChildrenLayoutTransition = LayoutTransition.Ease(300, Easing.EaseInOut),
        };

        int next = 1;

        Label Chip()
        {
            int n = next++;
            return new Label
            {
                Text = $"Item {n}",
                Padding = new(14, 6),
                CornerRadius = new(14),
                Background = Palette[n % Palette.Length],
                TextColor = Colors.White,
                EnterTransition = VisibilityTransition.FadeScale(250, 0.6f),
                ExitTransition = VisibilityTransition.Fade(200),
            };
        }

        for (int i = 0; i < 8; i++)
            wrap.Children.Add(Chip());

        var random = new Random(3);

        var add = new Button { Text = "Add" };
        add.Click += (_, _) => wrap.Children.Insert(random.Next(wrap.Children.Count + 1), Chip());

        var remove = new Button { Text = "Remove" };
        remove.Click += (_, _) => { if (wrap.Children.Count > 0) wrap.Children.RemoveAt(random.Next(wrap.Children.Count)); };

        var shuffle = new Button { Text = "Shuffle" };
        shuffle.Click += (_, _) =>
        {
            if (wrap.Children.Count < 2) return;
            int from = random.Next(wrap.Children.Count);
            UIElement moved = wrap.Children[from];
            wrap.Children.RemoveAt(from);
            wrap.Children.Insert(random.Next(wrap.Children.Count + 1), moved);
        };

        var root = Stretch();
        root.Children.AddRange([Demo.Row(add, remove, shuffle), wrap]);
        return root;
    }

    private static UIElement Visibility()
    {
        var card = new Label
        {
            Text = "Now you see me",
            Padding = new(24, 16),
            CornerRadius = new(8),
            Background = Palette[1],
            TextColor = Colors.White,
            EnterTransition = VisibilityTransition.Slide(300, 0, -16),
            ExitTransition = VisibilityTransition.FadeScale(200, 0.9f),
        };

        var toggle = new ToggleSwitch { Text = "Visible", IsOn = true };
        toggle.Toggled += (_, _) => card.IsVisible = toggle.IsOn;

        return Demo.Column(toggle, card);
    }

    private static UIElement Loops()
    {
        var heart = new SvgIcon
        {
            PathData = "M12 21 L4.5 13.5 C2.5 11.5 2.5 8 4.5 6 C6.5 4 9.5 4 12 7 C14.5 4 17.5 4 19.5 6 C21.5 8 21.5 11.5 19.5 13.5 Z",
            Color = Palette[2],
            IconSize = 48,
            Size = new(64, 64),
        };

        var spinner = new Label
        {
            Text = "↻",
            Font = ZeppelinForms.Drawing.Font.Default.WithSize(36),
            Size = new(64, 64),
            HorizontalContentAlign = HorizontalContentAlignment.Center,
            VerticalContentAlign = VerticalContentAlignment.Center,
        };

        var start = new Button { Text = "Start" };
        start.Click += (_, _) =>
        {
            // a beat: up and back down twice a second
            heart.AnimateLoop("beat", TimeSpan.FromMilliseconds(600), phase =>
            {
                float scale = 1f + 0.15f * MathF.Sin(phase * MathF.PI);
                heart.ScaleX = scale;
                heart.ScaleY = scale;
            });

            spinner.AnimateLoop("spin", TimeSpan.FromSeconds(1.2), phase => spinner.Rotation = phase * 360f);
        };

        var stop = new Button { Text = "Stop" };
        stop.Click += (_, _) =>
        {
            heart.StopAnimation("beat");
            spinner.StopAnimation("spin");
            heart.ScaleX = heart.ScaleY = 1f;
            spinner.Rotation = 0f;
        };

        return Demo.Column(Demo.Row(start, stop), Demo.Row(heart, spinner));
    }

    private static StackPanel Stretch() => new()
    {
        Orientation = Orientation.Vertical,
        Spacing = 10,
        CrossAxisAlignment = CrossAxisAlignment.Stretch,
    };
}
