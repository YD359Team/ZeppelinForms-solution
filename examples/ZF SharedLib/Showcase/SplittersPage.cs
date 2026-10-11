using System.Globalization;
using ZeppelinForms.Core.Collections;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;

namespace ZF_SharedLib.Showcase;

/// <summary>Space the user divides with a splitter, and layouts that change with the
/// space they get.</summary>
internal static class SplittersPage
{
    public static UIElement Create() =>
        Demo.Page("Splitters and adaptive", "Areas resized by dragging, and layouts that rebuild for a phone, a tablet or a desktop.",
            Demo.Section("Split container", Split(), "Drag the line, or focus it and use the arrows; FixedPanel keeps one side's size as the whole resizes"),
            Demo.Section("Grid splitter", GridSplit(), "A GridSplitter in its own Auto track resizes the tracks on both sides of it"),
            Demo.Section("Adaptive layout", Adaptive(), "AdaptiveLayout builds its content for the size class: Compact under 600, Expanded from 1000"),
            Demo.Section("Layout builder", Builder(), "LayoutBuilder gets the available size and returns the content made for it"));

    private static UIElement Split()
    {
        Label ratio = Demo.Note("The ratio: 0.30");

        // an editor's layout: a side bar, and the code over the output
        var inner = new SplitContainer
        {
            Orientation = Orientation.Vertical,
            SplitterRatio = 0.65f,
            FirstMinSize = 40,
            SecondMinSize = 40,
            First = Area("Code"),
            Second = Area("Output"),
        };

        var outer = new SplitContainer
        {
            Orientation = Orientation.Horizontal,
            SplitterRatio = 0.3f,
            FirstMinSize = 80,
            SecondMinSize = 120,
            Size = new(float.NaN, 260),
            First = Area("Files"),
            Second = inner,
        };

        outer.SplitterMoved += (_, _) => ratio.Text = $"The ratio: {outer.SplitterRatio.ToString("0.00", CultureInfo.CurrentCulture)}";

        var fixedPanel = new ComboBox { Items = { "None", "First", "Second" }, SelectedIndex = 0 };
        fixedPanel.SelectionChanged += (_, _) => outer.FixedPanel = (SplitterFixedPanel)fixedPanel.SelectedIndex;

        var root = Stretch();
        root.Children.AddRange([Demo.Row(Demo.Labeled("Fixed panel", fixedPanel), ratio), outer]);
        return root;
    }

    private static UIElement GridSplit()
    {
        var grid = new Grid
        {
            Columns = "*,Auto,2*",
            Rows = "*",
            Size = new(float.NaN, 160),
        };

        grid.Children.Add(Area("One share"), 0, 0);
        grid.Children.Add(new GridSplitter { Orientation = Orientation.Vertical }, 0, 1);
        grid.Children.Add(Area("Two shares"), 0, 2);

        return grid;
    }

    private static UIElement Adaptive()
    {
        var adaptive = new AdaptiveLayout
        {
            Content = sizeClass =>
            {
                // the same three cards: under one another on a phone, two and one
                // on a tablet, side by side on a desktop
                int columns = sizeClass switch
                {
                    SizeClass.Compact => 1,
                    SizeClass.Medium => 2,
                    _ => 3,
                };

                var grid = new UniformGrid { Columns = columns, SpacingX = 8, SpacingY = 8 };
                grid.Children.AddRange([Card("Inbox", sizeClass), Card("Calendar", sizeClass), Card("Contacts", sizeClass)]);
                return grid;
            },
        };

        return adaptive;

        static UIElement Card(string title, SizeClass sizeClass)
        {
            var card = Area($"{title}\n{sizeClass}");
            card.Size = new(float.NaN, 80);
            return card;
        }
    }

    private static UIElement Builder() => new LayoutBuilder
    {
        Builder = size => new Label
        {
            // a narrow screen gets the facts one per line; a wide one, all on one
            Text = size.Width < 480
                ? $"{size.Width.ToString("0", CultureInfo.CurrentCulture)} px across\na narrow screen\none fact per line"
                : $"{size.Width.ToString("0", CultureInfo.CurrentCulture)} px across — room for everything on one line",
        },
    };

    /// <summary>A named area to divide.</summary>
    private static Label Area(string text) => new()
    {
        Text = text,
        Background = new Color(36, 13, 110, 253),
        BorderColor = new Color(110, 13, 110, 253),
        BorderWidth = 1,
        CornerRadius = new(4),
        HorizontalContentAlign = HorizontalContentAlignment.Center,
        VerticalContentAlign = VerticalContentAlignment.Center,
    };

    private static StackPanel Stretch() => new()
    {
        Orientation = Orientation.Vertical,
        Spacing = 10,
        CrossAxisAlignment = CrossAxisAlignment.Stretch,
    };
}
