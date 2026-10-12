using ZeppelinForms.Core.Collections;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;

namespace ZF_SharedLib.Showcase;

/// <summary>Text: the type ramp, fonts and decorations, runs of mixed styles, hints,
/// and Markdown.</summary>
internal static class TextPage
{
    private const string SampleMarkdown = """
        # Markdown

        MarkdownViewer lays out **bold**, *italic*, `code` and [links](https://github.com/YD359Team/ZeppelinForms-solution).

        ## Lists

        - Bullets
          - nested
        1. Numbers
        2. in order

        - [x] Tasks that are done
        - [ ] and those that are not

        > A quote stands out with a bar beside it.

        ```csharp
        var label = new Label { Text = "Hello" };
        ```

        | Control | Since |
        |---------|------:|
        | Label   | 0.1   |
        | Markdown viewer | 0.12 |

        ---

        Text wraps at the edge of the viewer: narrow the window and it flows to the next lines.
        """;

    public static UIElement Create()
    {
        return Demo.Page("Text", "How text is set: styles, fonts, decorations, mixed runs, hints and Markdown.",
            Demo.Section("Type ramp", Ramp(), "TextStyle picks size and weight from the theme's ramp, so the whole application changes together"),
            Demo.Section("Fonts", Fonts()),
            Demo.Section("Wrapping and trimming", Wrapping(), "TextWrapping breaks the rows at the edge; TextTrimming shortens what doesn't fit with \"…\"; MaxLines caps the rows"),
            Demo.Section("Decorations", Decorations(), "TextDecorations, TextOutline and TextTransform are inherited: set them on a panel for everything in it"),
            Demo.Section("Mixed runs", Runs(), "RichLabel sets a line from runs, each with its own font, color and background"),
            Demo.Section("Hints", Hints(), "Hover the dotted words"),
            Demo.Section("Markdown", Markdown(), "Headings, lists, tasks, quotes, code, tables and links"));
    }

    private static UIElement Ramp()
    {
        var column = Demo.Column();
        column.Spacing = 6;

        foreach (TextStyle style in Enum.GetValues<TextStyle>().Where(s => s != TextStyle.None))
            column.Children.Add(new Label { Text = style.ToString(), TextStyle = style });

        return column;
    }

    private static UIElement Fonts() => Demo.Column(
        new Label { Text = "The default font", Font = Font.Default },
        new Label { Text = "Light", Font = Font.Default.Light() },
        new Label { Text = "SemiBold", Font = Font.Default.SemiBold() },
        new Label { Text = "Bold", Font = Font.Default.Bold() },
        new Label { Text = "Italic", Font = Font.Default.Italic() },
        new Label { Text = "Monospace: 0O 1lI {}[]", Font = Font.Monospace },
        new Label { Text = "Larger, 24 px", Font = Font.Default.WithSize(24) });

    private static UIElement Wrapping()
    {
        const string Text = "A zeppelin is a rigid airship: a frame of girders under a fabric skin, with gas cells inside it and engines along its hull.";

        // a box whose width the slider sets: the labels follow it
        var box = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 10,
            CrossAxisAlignment = CrossAxisAlignment.Stretch,
            Size = new(320, float.NaN),
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        box.Children.AddRange([
            Demo.Labeled("Wrap", new Label { Text = Text, TextWrapping = TextWrapping.Wrap }),
            Demo.Labeled("Character ellipsis", new Label { Text = Text, TextTrimming = TextTrimming.CharacterEllipsis }),
            Demo.Labeled("Word ellipsis", new Label { Text = Text, TextTrimming = TextTrimming.WordEllipsis }),
            Demo.Labeled("Wrap, two rows at most", new Label { Text = Text, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.WordEllipsis, MaxLines = 2 }),
        ]);

        var width = new TrackBar { Minimum = 120, Maximum = 600, Value = 320, Size = new(220, float.NaN) };
        width.ValueChanged += (_, _) => box.Size = new(width.Value, float.NaN);

        return Demo.Column(Demo.Labeled("Width of the box", width), box);
    }

    private static UIElement Decorations() => Demo.Row(
        new Label { Text = "Underline", TextDecorations = TextDecorations.Underline },
        new Label { Text = "Strikethrough", TextDecorations = TextDecorations.Strikethrough },
        new Label { Text = "Both", TextDecorations = TextDecorations.Underline | TextDecorations.Strikethrough },
        new Label { Text = "Colored line", TextDecorations = TextDecorations.Underline, TextDecorationColor = new Color(255, 0xDC, 0x35, 0x45) },
        new Label { Text = "Outline", Font = Font.Default.WithSize(24).Bold(), TextColor = Colors.White, TextOutlineColor = new Color(255, 0x0D, 0x6E, 0xFD), TextOutlineWidth = 2 },
        new Label { Text = "upper case", TextTransform = TextTransform.UpperCase });

    private static UIElement Runs()
    {
        var label = new RichLabel();
        label.SetText(
            "Mix ",
            TextRun.Bold("bold"),
            ", ",
            TextRun.Italic("italic"),
            ", ",
            TextRun.Colored("color", new Color(255, 0xD6, 0x33, 0x84)),
            ", ",
            new TextRun("highlight") { Background = new Color(120, 0xFF, 0xC1, 0x07) },
            ", ",
            new TextRun("struck") { Strikethrough = true },
            " and ",
            new TextRun("larger") { Font = Font.Default.WithSize(22) },
            " in one line.");

        return label;
    }

    private static UIElement Hints() => Demo.Row(
        new Label { Text = "A" },
        new HintLabel { Text = "tooltip", Hint = "A hint opens over the text on hover or on tap" },
        new Label { Text = "explains a" },
        new HintLabel { Text = "term", Hint = "The text of a hint can be long; it wraps at MaxHintWidth" },
        new Label { Text = "in place." });

    private static UIElement Markdown()
    {
        Label link = Demo.Note("Click a link");

        var viewer = new MarkdownViewer
        {
            Markdown = SampleMarkdown,
            Size = new(float.NaN, 420),
            OverflowY = Overflow.Auto,
        };

        // the viewer doesn't open links itself: the application decides
        viewer.LinkClicked += (_, e) => link.Text = $"Link: {e.Url}";

        var root = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8, CrossAxisAlignment = CrossAxisAlignment.Stretch };
        root.Children.AddRange([viewer, link]);
        return root;
    }
}
