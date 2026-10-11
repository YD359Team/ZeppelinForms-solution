using ZeppelinForms.Core.Collections;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;

namespace ZF_SharedLib.Showcase;

/// <summary>
/// The pieces every page of the showcase is made of, so that all of them look and
/// read alike: a page is a title, a line on what it shows and sections one under
/// another; a section is a caption over a card with the demo in it.
/// </summary>
internal static class Demo
{
    /// <summary>The showcase's own look, on top of the theme: the cards around the
    /// demos and the quieter texts. Theme colors (<c>@Border</c>, <c>@TextSecondary</c>)
    /// keep it right in every theme, dark and high contrast included.</summary>
    public const string Sheet = """
        /* the showcase's own look: cards around the demos, quieter texts beside them */
        Border.demo-card {
            BorderWidth: 1;
            BorderColor: @Border;
            CornerRadius: 8;
            Padding: 16;
        }

        Label.demo-description { TextColor: @TextSecondary; }
        Label.demo-note { TextColor: @TextSecondary; }
        """;

    /// <summary>A page: the title, what it shows, its sections. It scrolls when they
    /// are taller than the window.</summary>
    public static StackPanel Page(string title, string description, params UIElement[] sections)
    {
        var page = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 20,
            Padding = new(24, 20),
            CrossAxisAlignment = CrossAxisAlignment.Stretch,
            OverflowY = Overflow.Auto,
            ScrollBarMode = ScrollBarMode.Inline,
        };

        var header = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 4,
            CrossAxisAlignment = CrossAxisAlignment.Start,
        };

        header.Children.Add(new Label { Text = title, TextStyle = TextStyle.Title });

        var about = new Label { Text = description };
        about.Classes.Add("demo-description");
        header.Children.Add(about);

        page.Children.Add(header);
        page.Children.AddRange(sections);

        return page;
    }

    /// <summary>A demo: a caption, the card with the controls, an optional note under
    /// it — what to try, what the demo shows.</summary>
    public static StackPanel Section(string caption, UIElement content, string? note = null)
    {
        var section = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 8,
            CrossAxisAlignment = CrossAxisAlignment.Stretch,
        };

        section.Children.Add(new Label
        {
            Text = caption,
            TextStyle = TextStyle.Subtitle,
            HorizontalAlignment = HorizontalAlignment.Left,
        });

        var card = new Border { Child = content };
        card.Classes.Add("demo-card");
        section.Children.Add(card);

        if (note is not null)
            section.Children.Add(Note(note));

        return section;
    }

    /// <summary>Controls side by side, wrapping onto the next line when the window is narrow.</summary>
    public static WrapPanel Row(params UIElement[] items)
    {
        var row = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            LineSpacing = 12,
            LineAlignment = CrossAxisAlignment.Center,
        };

        row.Children.AddRange(items);
        return row;
    }

    /// <summary>Controls one under another, at the start of the line.</summary>
    public static StackPanel Column(params UIElement[] items)
    {
        var column = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 10,
            CrossAxisAlignment = CrossAxisAlignment.Start,
        };

        column.Children.AddRange(items);
        return column;
    }

    /// <summary>A quiet line: a hint, a readout of what the demo did.</summary>
    public static Label Note(string text)
    {
        var note = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Left };
        note.Classes.Add("demo-note");
        return note;
    }

    /// <summary>A control with a caption above it.</summary>
    public static StackPanel Labeled(string caption, UIElement control) =>
        Column(new Label { Text = caption, TextStyle = TextStyle.Caption }, control);
}
