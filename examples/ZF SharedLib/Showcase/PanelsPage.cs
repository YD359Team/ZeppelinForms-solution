using ZeppelinForms.Animation;
using ZeppelinForms.Core.Collections;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;

namespace ZF_SharedLib.Showcase;

/// <summary>The panels that lay out their children: in a line, wrapping, in a grid,
/// docked to the edges, in equal cells and at free positions.</summary>
internal static class PanelsPage
{
    private static readonly Color[] Palette =
    [
        new(255, 0x0D, 0x6E, 0xFD),
        new(255, 0x66, 0x10, 0xF2),
        new(255, 0xD6, 0x33, 0x84),
        new(255, 0xFD, 0x7E, 0x14),
        new(255, 0x19, 0x87, 0x54),
        new(255, 0x20, 0xC9, 0x97),
    ];

    public static UIElement Create() =>
        Demo.Page("Panels", "Every panel lays its children out its own way; together they make any screen.",
            Demo.Section("Stack panel", Stack(), "A line of children; the alignments say where along it and across it they go"),
            Demo.Section("Wrap panel", Wrap(), "Children in lines, moving to the next when the line is full: narrow the window"),
            Demo.Section("Grid", GridDemo(), "Tracks of Auto (by content), fixed pixels or Star (shares of the rest); a child can span several"),
            Demo.Section("Dock panel", Dock(), "Each child docks to an edge of what is left; Fill takes the rest"),
            Demo.Section("Uniform grid", Uniform(), "Equal cells; FirstColumn leaves the first cells of the first row empty"),
            Demo.Section("Free positions", Free(), "Panel puts each child at its Position; LayoutTransition moves it there smoothly"));

    private static UIElement Stack()
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Size = new(float.NaN, 140),
            Padding = new(8),
            Background = new Color(20, 128, 128, 128),
            CornerRadius = new(6),
            ChildrenLayoutTransition = LayoutTransition.Ease(200, Easing.EaseInOut),
        };
        stack.Children.AddRange([Block("One", 0), Block("A longer one", 1), Block("Three", 2)]);

        var orientation = Choice(["Horizontal", "Vertical"], 0, i => stack.Orientation = (Orientation)i);
        var main = Choice(Enum.GetNames<MainAxisAlignment>(), 0, i => stack.MainAxisAlignment = (MainAxisAlignment)i);
        var cross = Choice(Enum.GetNames<CrossAxisAlignment>(), 0, i => stack.CrossAxisAlignment = (CrossAxisAlignment)i);

        var spacing = new TrackBar { Minimum = 0, Maximum = 32, Value = 8, Size = new(140, float.NaN) };
        spacing.ValueChanged += (_, _) => stack.Spacing = spacing.Value;

        var root = Fill();
        root.Children.AddRange([
            Demo.Row(
                Demo.Labeled("Orientation", orientation),
                Demo.Labeled("Main axis", main),
                Demo.Labeled("Cross axis", cross),
                Demo.Labeled("Spacing", spacing)),
            stack,
        ]);
        return root;
    }

    private static UIElement Wrap()
    {
        string[] tags = ["C#", ".NET 10", "SkiaSharp", "Windows", "Linux", "Android", "Browser", "WebAssembly", "Themes", "Styles", "Animation", "Charts", "Maps", "Accessibility", "Localization", "Hot reload"];

        var wrap = new WrapPanel { Spacing = 8, LineSpacing = 8 };

        for (int i = 0; i < tags.Length; i++)
        {
            wrap.Children.Add(new Label
            {
                Text = tags[i],
                Padding = new(12, 4),
                CornerRadius = new(12),
                Background = Palette[i % Palette.Length],
                TextColor = Colors.White,
            });
        }

        return wrap;
    }

    private static UIElement GridDemo()
    {
        // a form: captions by their content, fields share the rest
        var form = new Grid
        {
            Columns = "Auto,*",
            Rows = "Auto,Auto,Auto",
            Size = new(420, float.NaN),
        };

        string[] captions = ["Name", "E-mail", "Comment"];
        for (int row = 0; row < captions.Length; row++)
        {
            form.Children.Add(new Label { Text = captions[row], Margin = new(0, 4, 12, 4), VerticalAlignment = VerticalAlignment.Center }, row, 0);
            form.Children.Add(new TextBox { Margin = new(0, 4) }, row, 1);
        }

        // spans: a header across the top, a sidebar down the side
        var layout = new Grid
        {
            Columns = "120,*,*",
            Rows = "40,80,80",
            Size = new(float.NaN, 200),
        };

        Label header = Block("Header — ColumnSpan 3", 0);
        header.ColumnSpan = 3;
        layout.Children.Add(header, 0, 0);

        Label side = Block("RowSpan 2", 1);
        side.RowSpan = 2;
        layout.Children.Add(side, 1, 0);

        layout.Children.Add(Block("1 × 1", 2), 1, 1);
        layout.Children.Add(Block("1 × 2", 3), 1, 2);

        Label wide = Block("Star columns share the width", 4);
        wide.ColumnSpan = 2;
        layout.Children.Add(wide, 2, 1);

        var root = Fill();
        root.Children.AddRange([form, layout]);
        return root;
    }

    private static UIElement Dock()
    {
        Label top = Block("Top", 0), bottom = Block("Bottom", 1), left = Block("Left", 2), right = Block("Right", 3), fill = Block("Fill", 4);

        top.Docking = ZeppelinForms.Forms.Enums.Dock.Top;
        bottom.Docking = ZeppelinForms.Forms.Enums.Dock.Bottom;
        left.Docking = ZeppelinForms.Forms.Enums.Dock.Left;
        right.Docking = ZeppelinForms.Forms.Enums.Dock.Right;
        fill.Docking = ZeppelinForms.Forms.Enums.Dock.Fill;

        var dock = new DockPanel { Size = new(float.NaN, 200) };
        dock.Children.AddRange([top, bottom, left, right, fill]);
        return dock;
    }

    private static UIElement Uniform()
    {
        var grid = new UniformGrid { Columns = 3, SpacingX = 6, SpacingY = 6, FirstColumn = 1, Size = new(300, float.NaN) };

        for (int i = 1; i <= 8; i++)
            grid.Children.Add(Block(i.ToString(System.Globalization.CultureInfo.InvariantCulture), i));

        var first = new NumericUpDown { Minimum = 0, Maximum = 2, Value = 1 };
        first.ValueChanged += (_, _) => grid.FirstColumn = (int)first.Value;

        return Demo.Column(Demo.Labeled("FirstColumn", first), grid);
    }

    private static UIElement Free()
    {
        var canvas = new Panel
        {
            Size = new(float.NaN, 220),
            Background = new Color(20, 128, 128, 128),
            CornerRadius = new(6),
            ChildrenLayoutTransition = LayoutTransition.Ease(450, Easing.EaseInOut),
        };

        var random = new Random(7);
        var blocks = Enumerable.Range(0, 6).Select(i => Block($"{i + 1}", i)).ToList();
        foreach (Label block in blocks)
        {
            block.Size = new(64, 48);
            canvas.Children.Add(block);
        }

        void Shuffle()
        {
            float width = Math.Max(canvas.ActualSize.Width, 300) - 64;
            foreach (Label block in blocks)
                block.Position = new(random.NextSingle() * width, random.NextSingle() * (220 - 48));
        }

        Shuffle();

        var shuffle = new Button { Text = "Shuffle" };
        shuffle.Click += (_, _) => Shuffle();

        var root = Fill();
        root.Children.AddRange([Demo.Row(shuffle), canvas]);
        return root;
    }

    /// <summary>A colored block with a caption: what the panels lay out.</summary>
    private static Label Block(string text, int color) => new()
    {
        Text = text,
        Background = Palette[color % Palette.Length],
        TextColor = Colors.White,
        CornerRadius = new(4),
        Padding = new(16, 10),
        Margin = new(2),
        HorizontalContentAlign = HorizontalContentAlignment.Center,
        VerticalContentAlign = VerticalContentAlignment.Center,
    };

    private static ComboBox Choice(string[] options, int selected, Action<int> apply)
    {
        var combo = new ComboBox { SelectedIndex = -1 };
        combo.Items.AddRange(options);
        combo.SelectedIndex = selected;
        combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) apply(combo.SelectedIndex); };
        return combo;
    }

    private static StackPanel Fill() => new()
    {
        Orientation = Orientation.Vertical,
        Spacing = 12,
        CrossAxisAlignment = CrossAxisAlignment.Stretch,
    };
}
