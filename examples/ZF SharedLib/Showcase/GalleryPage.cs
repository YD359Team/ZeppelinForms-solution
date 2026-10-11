using ZeppelinForms.Core.Collections;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;

namespace ZF_SharedLib.Showcase;

/// <summary>A gallery of pictures that load as they come into view, with selection
/// and a viewer to open them in.</summary>
internal static class GalleryPage
{
    private static readonly string[] Titles =
    [
        "Dawn", "Harbor", "Dunes", "Glacier", "Meadow", "Canyon", "Lagoon", "Tundra",
        "Orchard", "Reef", "Steppe", "Fjord", "Savanna", "Delta", "Marsh", "Plateau",
        "Archipelago", "Valley", "Mesa", "Taiga", "Atoll", "Moor", "Bayou", "Highland",
    ];

    public static UIElement Create()
    {
        Label status = Demo.Note("Double-click or Enter opens a picture; Esc closes it");

        var gallery = new ImageGallery
        {
            Size = new(float.NaN, 380),
            ThumbnailWidth = 160,
            ThumbnailHeight = 110,
            ShowTitles = true,
            SelectionMode = SelectionMode.Extended,
        };

        for (int i = 0; i < Titles.Length; i++)
        {
            int seed = i;

            gallery.Items.Add(new GalleryItem
            {
                Title = Titles[i],
                Description = $"A generated landscape, number {i + 1}",

                // the pictures come slowly, as from a network: the cells show
                // a placeholder until theirs arrives
                ThumbnailLoader = async cancel =>
                {
                    await Task.Delay(150 + seed * 37 % 400, cancel);
                    return Landscape(seed, 240, 165);
                },
                ImageLoader = async cancel =>
                {
                    await Task.Delay(200, cancel);
                    return Landscape(seed, 960, 660);
                },
            });
        }

        gallery.SelectionChanged += (_, _) =>
            status.Text = gallery.SelectedIndices.Count switch
            {
                0 => "Nothing selected",
                1 => $"Selected: {gallery.SelectedItem?.Title}",
                int n => $"{n} selected",
            };

        gallery.ItemActivated += (_, e) => status.Text = $"Opened: {e.Item.Title}";

        var mode = new ComboBox { Items = { "Single", "Multiple", "Extended" }, SelectedIndex = 2 };
        mode.SelectionChanged += (_, _) => gallery.SelectionMode = (SelectionMode)mode.SelectedIndex;

        var titles = new ToggleSwitch { Text = "Titles", IsOn = true };
        titles.Toggled += (_, _) => gallery.ShowTitles = titles.IsOn;

        var fit = new ToggleSwitch { Text = "Fit whole" };
        fit.Toggled += (_, _) => gallery.Stretch = fit.IsOn ? GalleryStretch.Uniform : GalleryStretch.Fill;

        var size = new TrackBar { Minimum = 100, Maximum = 260, Value = 160, Size = new(180, float.NaN) };
        size.ValueChanged += (_, _) =>
        {
            gallery.ThumbnailWidth = size.Value;
            gallery.ThumbnailHeight = size.Value * 0.69f;
        };

        var demo = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 10,
            CrossAxisAlignment = CrossAxisAlignment.Stretch,
        };
        demo.Children.AddRange([
            Demo.Row(Demo.Labeled("Selection", mode), Demo.Labeled("Thumbnail size", size), titles, fit),
            gallery,
            status,
        ]);

        return Demo.Page("Gallery", "Thumbnails in a grid that fills the width, loaded as they come into view.",
            Demo.Section("Image gallery", demo, "ThumbnailLoader and ImageLoader load lazily; the viewer goes through the pictures with the arrows"));
    }

    /// <summary>A landscape drawn from a number: a sky in two colors, a sun and two
    /// rows of hills. The same number gives the same picture in any size.</summary>
    private static Image Landscape(int seed, int width, int height)
    {
        var random = new Random(seed * 7919);

        float hue = random.NextSingle() * 360f;
        (byte r, byte g, byte b) skyTop = Hsv(hue, 0.55f, 0.95f);
        (byte r, byte g, byte b) skyBottom = Hsv(hue + 40f, 0.35f, 1f);
        (byte r, byte g, byte b) far = Hsv(hue + 160f, 0.35f, 0.55f);
        (byte r, byte g, byte b) near = Hsv(hue + 180f, 0.5f, 0.35f);
        (byte r, byte g, byte b) sun = Hsv(hue + 20f, 0.25f, 1f);

        float sunX = (0.2f + random.NextSingle() * 0.6f) * width;
        float sunY = (0.2f + random.NextSingle() * 0.2f) * height;
        float sunR = height * 0.12f;

        float phase1 = random.NextSingle() * 6f, phase2 = random.NextSingle() * 6f;

        var pixels = new byte[width * height * 4];

        for (int x = 0; x < width; x++)
        {
            float u = x / (float)width;
            float farLine = height * (0.58f + 0.08f * MathF.Sin(u * 5f + phase1));
            float nearLine = height * (0.74f + 0.07f * MathF.Sin(u * 8f + phase2));

            for (int y = 0; y < height; y++)
            {
                (byte r, byte g, byte b) c;

                if (y >= nearLine) c = near;
                else if (y >= farLine) c = far;
                else
                {
                    float dx = x - sunX, dy = y - sunY;
                    c = dx * dx + dy * dy <= sunR * sunR ? sun : Mix(skyTop, skyBottom, y / farLine);
                }

                int i = (y * width + x) * 4;
                pixels[i] = c.r;
                pixels[i + 1] = c.g;
                pixels[i + 2] = c.b;
                pixels[i + 3] = 255;
            }
        }

        return new Image(width, height, pixels);
    }

    private static (byte, byte, byte) Mix((byte r, byte g, byte b) a, (byte r, byte g, byte b) b, float t) =>
        ((byte)(a.r + (b.r - a.r) * t), (byte)(a.g + (b.g - a.g) * t), (byte)(a.b + (b.b - a.b) * t));

    private static (byte, byte, byte) Hsv(float h, float s, float v)
    {
        h = ((h % 360f) + 360f) % 360f / 60f;
        float c = v * s, x = c * (1 - MathF.Abs(h % 2 - 1)), m = v - c;

        (float r, float g, float b) = (int)h switch
        {
            0 => (c, x, 0f),
            1 => (x, c, 0f),
            2 => (0f, c, x),
            3 => (0f, x, c),
            4 => (x, 0f, c),
            _ => (c, 0f, x),
        };

        return ((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
