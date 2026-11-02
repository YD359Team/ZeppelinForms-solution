using System.Globalization;
using ZeppelinForms;
using ZeppelinForms.Core.Collections;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Effects;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;

namespace ZF_SharedLib.Showcase;

/// <summary>Visual effects: glass, blur, reflection, transforms, shadows, opacity
/// and the glitch.</summary>
internal static class EffectsPage
{
    public static UIElement Create() =>
        Demo.Page("Effects", "How an element gets onto the screen: through glass, blurred, mirrored, turned, with a shadow.",
            Demo.Section("Over a picture", Gallery(), "Acrylic blurs what is under the element; Blur blurs the element itself"),
            Demo.Section("Tune them", Tuning(), "Effects are a chain on element.Effects; each is replaced as the sliders move"),
            Demo.Section("Shadows", Shadows(), "BoxShadow is a property, so a style sheet or a transition can set it"),
            Demo.Section("Light", Light(), "Glow lights the border — standing, or a comet running round it; Halo is light behind an element, of its own shape or round. With reduced motion the comet stands"),
            Demo.Section("Gradients", Gradients(), "Background gradients go along a line; FillRadialGradient and FillSweepGradient draw them round a center"),
            Demo.Section("Opacity", Opacity()),
            Demo.Section("Blend modes", Blending(), "BlendMode puts an element over what is under it as Avalonia's BitmapBlendingMode and CSS mix-blend-mode do"),
            Demo.Section("Glitch", Glitch(), "element.Glitch() and element.StopGlitch()"));

    private static UIElement Gallery()
    {
        // a backdrop: over one flat color neither acrylic nor a reflection shows
        var backdrop = new PictureBox
        {
            RowSpan = 2,
            ColumnSpan = 2,
            Layout = ZeppelinForms.Drawing.Imaging.ImageLayout.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        backdrop.LoadAsset("Laughing.png");

        var grid = new Grid
        {
            Columns = "*,*",
            Rows = "*,*",
            Size = new(float.NaN, 420),
            CornerRadius = new(8),
        };

        grid.Children.Add(backdrop);
        grid.Children.Add(AcrylicCard(), 0, 0);
        grid.Children.Add(BlurCard(), 0, 1);
        grid.Children.Add(ReflectionCard(), 1, 0);
        grid.Children.Add(TransformCard(), 1, 1);

        return grid;
    }

    private static StackPanel AcrylicCard()
    {
        var glass = new StackPanel
        {
            Background = Colors.Transparent,
            CornerRadius = new(12f),
            Padding = new(16),
            Size = new(220, 96),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 4,
        };

        glass.Children.AddRange([
            new Label { Text = "AcrylicEffect", TextColor = Colors.Black, TextStyle = TextStyle.BodyStrong },
            new Label { Text = "blur, tint and noise", TextColor = Colors.Black },
        ]);

        glass.Effects.Add(new AcrylicEffect
        {
            BlurRadius = 24f,
            TintColor = new Color(150, 255, 255, 255),
            NoiseOpacity = 0.05f,
        });

        return glass;
    }

    /// <summary>Blurs the element itself, not what is under it.</summary>
    private static Label BlurCard()
    {
        var label = new Label
        {
            Text = "BlurEffect",
            Font = Font.Default.WithSize(28f),
            TextColor = Colors.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        label.Effects.Add(new BlurEffect(3f));
        return label;
    }

    /// <summary>The reflection goes down, so the picture keeps to the top of its
    /// cell: room must be left under it inside the parent's bounds.</summary>
    private static PictureBox ReflectionCard()
    {
        var picture = new PictureBox
        {
            Size = new(120, 120),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new(0, 10, 0, 0),
        };

        picture.SetImage(Assets.LogoImage(240));

        picture.Effects.Add(new ReflectionEffect
        {
            Height = 0.45f,
            Gap = 3f,
            StartOpacity = 0.4f,
        });

        return picture;
    }

    /// <summary>Rotation, skew and scale in one effect.</summary>
    private static Label TransformCard()
    {
        var label = new Label
        {
            Text = "TransformEffect",
            Font = Font.Default.WithSize(20f),
            TextColor = Colors.White,
            Background = new Color(160, 0, 0, 0),
            Padding = new(12, 8),
            CornerRadius = new(6f),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        label.Effects.Add(new TransformEffect
        {
            Rotation = -12f,
            ScaleX = 1.15f,
            ScaleY = 1.15f,
            SkewX = 0.1f,
        });

        return label;
    }

    private static UIElement Tuning()
    {
        var subject = new Label
        {
            Text = "Zeppelin",
            Font = Font.Default.WithSize(28).Bold(),
            Padding = new(24, 12),
            CornerRadius = new(8),
            Background = new Color(255, 0x0D, 0x6E, 0xFD),
            TextColor = Colors.White,
        };

        var blur = Slider(0, 8, 0);
        var rotation = Slider(-45, 45, 0);
        var scale = Slider(50, 150, 100);
        var skew = Slider(-30, 30, 0);

        void Apply()
        {
            subject.Effects.Clear();

            subject.Effects.Add(new TransformEffect
            {
                Rotation = rotation.Value,
                ScaleX = scale.Value / 100f,
                ScaleY = scale.Value / 100f,
                SkewX = skew.Value / 100f,
            });

            if (blur.Value > 0)
                subject.Effects.Add(new BlurEffect(blur.Value));
        }

        foreach (TrackBar slider in new[] { blur, rotation, scale, skew })
            slider.ValueChanged += (_, _) => Apply();

        Apply();

        var stage = new Border
        {
            Size = new(float.NaN, 200),
            Child = new StackPanel
            {
                Orientation = Orientation.Vertical,
                MainAxisAlignment = MainAxisAlignment.Center,
                CrossAxisAlignment = CrossAxisAlignment.Center,
                Children = { subject },
            },
        };

        var root = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10, CrossAxisAlignment = CrossAxisAlignment.Stretch };
        root.Children.AddRange([
            Demo.Row(Demo.Labeled("Blur", blur), Demo.Labeled("Rotation", rotation), Demo.Labeled("Scale, %", scale), Demo.Labeled("Skew, %", skew)),
            stage,
        ]);
        return root;

        static TrackBar Slider(float min, float max, float value) =>
            new() { Minimum = min, Maximum = max, Value = value, Size = new(150, float.NaN) };
    }

    private static UIElement Shadows()
    {
        Border Card(string text, BoxShadow shadow) => new()
        {
            Size = new(140, 80),
            CornerRadius = new(8),
            Background = Colors.White,
            BoxShadow = shadow,
            Margin = new(8, 8, 8, 16),
            Child = new Label
            {
                Text = text,
                TextColor = Colors.Black,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        return Demo.Row(
            Card("Small", BoxShadow.Small),
            Card("Medium", BoxShadow.Medium),
            Card("Large", BoxShadow.Large),
            Card("Colored", new BoxShadow(0, 6, 16, 0, new Color(140, 0x0D, 0x6E, 0xFD))),
            Card("Spread", new BoxShadow(0, 0, 0, 4, new Color(255, 0xD6, 0x33, 0x84))));
    }

    private static UIElement Light()
    {
        Border Tile(string text, Action<Border> light)
        {
            var tile = new Border
            {
                Size = new(150, 80),
                CornerRadius = new(10),
                Background = new Color(255, 0x10, 0x14, 0x24),
                Margin = new(18),
                Child = new Label
                {
                    Text = text,
                    TextColor = Colors.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            light(tile);
            return tile;
        }

        var cyan = new Color(255, 0x00, 0xE5, 0xFF);
        var pink = new Color(255, 0xFF, 0x2D, 0x95);
        var gold = new Color(255, 0xFF, 0xC8, 0x3D);

        // the halo of a glyph's own shape: the star's strokes glow, not a box round it
        var star = new SvgIcon
        {
            PathData = "M12 2 L15 9 L22 9.5 L16.5 14 L18.5 21 L12 17 L5.5 21 L7.5 14 L2 9.5 L9 9 Z",
            Color = gold,
            IconSize = 56,
            Margin = new(24),
            Halo = new Halo(gold, 14),
        };

        var lamp = new Border
        {
            Size = new(56, 56),
            CornerRadius = new(28),
            Background = Colors.White,
            Margin = new(36),
            Halo = new Halo(new Color(200, 0xFF, 0xF4, 0xD6), 40, HaloShape.Radial),
        };

        // all of it on the dark, where light shows
        var stage = new Border
        {
            Background = new Color(255, 0x06, 0x08, 0x10),
            CornerRadius = new(12),
            Padding = new(12),
            Child = Demo.Row(
                Tile("Neon", t => t.Glow = new Glow(cyan, 14, 2)),
                Tile("Comet", t => t.Glow = new Glow(pink, 10, 2.5f, GlowMotion.Comet) { Period = TimeSpan.FromSeconds(2.5) }),
                Tile("Slow and long", t => t.Glow = new Glow(cyan, 8, 2, GlowMotion.Comet) { Period = TimeSpan.FromSeconds(6), CometLength = 0.6f }),
                star,
                lamp),
        };

        return stage;
    }

    /// <summary>Gradients round a center, drawn as a control draws: in DrawContent.</summary>
    private sealed class GradientSwatch(Action<Graphics, Rectangle> draw) : DecoratedControl
    {
        protected override void DrawContent(Graphics g) => draw(g, ContentBounds);
    }

    private static UIElement Gradients()
    {
        GradientStop[] sunset =
        [
            new(new Color(255, 0xFF, 0xD1, 0x66), 0f),
            new(new Color(255, 0xFF, 0x6B, 0x6B), 0.5f),
            new(new Color(255, 0x4D, 0x2C, 0x91), 1f),
        ];

        GradientStop[] wheel =
        [
            new(new Color(255, 255, 0, 0), 0f),
            new(new Color(255, 255, 255, 0), 1f / 6),
            new(new Color(255, 0, 255, 0), 2f / 6),
            new(new Color(255, 0, 255, 255), 3f / 6),
            new(new Color(255, 0, 0, 255), 4f / 6),
            new(new Color(255, 255, 0, 255), 5f / 6),
            new(new Color(255, 255, 0, 0), 1f),
        ];

        GradientSwatch Swatch(Action<Graphics, Rectangle> draw) => new(draw) { Size = new(120, 120), Margin = new(8) };

        return Demo.Row(
            Swatch((g, r) => g.FillRadialGradient(r, new CornerRadius(12), sunset,
                new Point(r.X + r.Width / 2f, r.Y + r.Height / 2f), r.Width / 2f)),
            Swatch((g, r) => g.FillRadialGradient(r, new CornerRadius(12), sunset,
                new Point(r.X + r.Width * 0.3f, r.Y + r.Height * 0.3f), r.Width)),
            Swatch((g, r) => g.FillSweepGradient(r, new CornerRadius(r.Width / 2f), wheel,
                new Point(r.X + r.Width / 2f, r.Y + r.Height / 2f), -90f)));
    }

    private static UIElement Opacity()
    {
        var card = new Label
        {
            Text = "Fading",
            Padding = new(24, 16),
            CornerRadius = new(8),
            Background = new Color(255, 0x19, 0x87, 0x54),
            TextColor = Colors.White,
        };

        Label value = Demo.Note("100%");

        var opacity = new TrackBar { Minimum = 0, Maximum = 100, Value = 100, Size = new(200, float.NaN) };
        opacity.ValueChanged += (_, _) =>
        {
            card.Opacity = opacity.Value / 100f;
            value.Text = (opacity.Value / 100f).ToString("P0", CultureInfo.CurrentCulture);
        };

        return Demo.Row(card, opacity, value);
    }

    private static UIElement Blending()
    {
        var backdrop = new PictureBox
        {
            Layout = ZeppelinForms.Drawing.Imaging.ImageLayout.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        backdrop.LoadAsset("Laughing.png");

        // three colored circles over the picture, each put over it in the mode picked
        Panel Circle(Color color, float x, float y) => new()
        {
            Size = new(140, 140),
            CornerRadius = new(70),
            Background = color,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new(x, y, 0, 0),
        };

        Panel[] circles =
        [
            Circle(new Color(255, 0xE5, 0x39, 0x35), 40, 30),
            Circle(new Color(255, 0x1E, 0x88, 0xE5), 130, 30),
            Circle(new Color(255, 0xFD, 0xD8, 0x35), 85, 110),
        ];

        var stage = new Grid { Size = new(float.NaN, 300), CornerRadius = new(8) };
        stage.Children.Add(backdrop);

        foreach (Panel circle in circles)
            stage.Children.Add(circle);

        var mode = new ComboBox { Size = new(160, float.NaN) };

        foreach (BlendMode each in Enum.GetValues<BlendMode>())
            mode.Items.Add(each);

        mode.SelectedIndex = (int)BlendMode.Multiply;

        void Apply()
        {
            foreach (Panel circle in circles)
                circle.BlendMode = mode.SelectedItem is BlendMode picked ? picked : BlendMode.Normal;
        }

        mode.SelectionChanged += (_, _) => Apply();
        Apply();

        // not Demo.Column: it keeps its children at their own width, and the stage,
        // which has none of its own, came out zero wide — the circles with it
        var root = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10, CrossAxisAlignment = CrossAxisAlignment.Stretch };
        root.Children.AddRange([Demo.Row(new Label { Text = "Mode:" }, mode), stage]);
        return root;
    }

    private static UIElement Glitch()
    {
        var logo = new PictureBox { Size = new(120, 120) };
        logo.SetImage(Assets.LogoImage(240));

        var title = new Label { Text = "SIGNAL LOST", Font = Font.Default.WithSize(32).Bold() };

        var on = new ToggleSwitch { Text = "Glitch" };
        var intensity = new TrackBar { Minimum = 10, Maximum = 200, Value = 100, Size = new(180, float.NaN) };

        void Apply()
        {
            foreach (UIElement element in new UIElement[] { logo, title })
            {
                if (on.IsOn) element.Glitch(intensity.Value / 100f);
                else element.StopGlitch();
            }
        }

        on.Toggled += (_, _) => Apply();
        intensity.ValueChanged += (_, _) => { if (on.IsOn) Apply(); };

        return Demo.Column(Demo.Row(on, Demo.Labeled("Intensity", intensity)), Demo.Row(logo, title));
    }
}
