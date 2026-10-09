using Xunit;
using ZeppelinForms.Accessibility;
using ZeppelinForms.Core.Text;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.UnitTests;

[Collection("Platform")]
public class MarkdownViewerTests
{
    // the headless measurer: a character is 0.6 of the size wide, a line 1.2 high
    private const float Char = 6f;

    private static (Form Form, MarkdownViewer Viewer) Create(string markdown, float width = 200, float height = 300, UIElement? after = null)
    {
        var viewer = new MarkdownViewer
        {
            Markdown = markdown,
            Font = new Font("Test", 10),
        };

        UIElement content = viewer;

        if (after is not null)
        {
            viewer.Size = new Size(width, height);
            content = new StackPanel { Children = { viewer, after } };
        }

        var form = new Form { Size = new Size(width, height), Content = content };

        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return (form, viewer);
    }

    private static string[] Texts(MarkdownViewer viewer) =>
        [.. viewer.Layout!.Lines.Select(line => string.Concat(line.Runs.Select(r => r.Text)))];

    private static Point Center(MarkdownViewer viewer, Rectangle content)
    {
        Rectangle local = viewer.FromContent(content);
        Point origin = viewer.GetAbsolutePosition();

        return new Point(origin.X + local.X + local.Width / 2f, origin.Y + local.Y + local.Height / 2f);
    }

    // ===== wrapping =====

    [Fact]
    public void AParagraphWrapsAtWordsToTheWidth()
    {
        // six words of four take 6 × 24 + 5 × 6 = 174, a seventh would not fit in 200
        (_, MarkdownViewer viewer) = Create(string.Join(' ', Enumerable.Repeat("word", 12)));

        Assert.Equal(
            ["word word word word word word", "word word word word word word"],
            Texts(viewer));
    }

    [Fact]
    public void AWordLongerThanTheLineIsCut()
    {
        (_, MarkdownViewer viewer) = Create(new string('x', 50));

        // 200 / 6 = 33 characters to a line
        Assert.Equal([33, 17], Texts(viewer).Select(t => t.Length));
    }

    [Fact]
    public void ALineBreakStartsANewLine()
    {
        (_, MarkdownViewer viewer) = Create("one  \ntwo");

        Assert.Equal(["one", "two"], Texts(viewer));
    }

    [Fact]
    public void EmphasisAndCodeAreRunsOfTheirOwnOnOneLine()
    {
        (_, MarkdownViewer viewer) = Create("a **b** `c`");

        MarkdownLine line = Assert.Single(viewer.Layout!.Lines);

        Assert.Equal(["a ", "b", " ", "c"], line.Runs.Select(r => r.Text));
        Assert.Equal(FontWeight.Bold, line.Runs[1].Font!.Weight);
        Assert.Equal(viewer.CodeBackground, line.Runs[3].Background);
    }

    [Fact]
    public void ACodeBlockKeepsItsIndentation()
    {
        (_, MarkdownViewer viewer) = Create("```\nif (x)\n    y();\n```");

        Assert.Equal(["if (x)", "    y();"], Texts(viewer));
    }

    [Fact]
    public void TheContentScrollsWhenLongerThanTheViewer()
    {
        (Form form, MarkdownViewer viewer) = Create(string.Join("\n\n", Enumerable.Range(0, 40).Select(i => $"Paragraph {i}")));

        Assert.True(viewer.Layout!.Height > 300);

        HeadlessInput.PressKey(form, Key.End);
        Assert.Equal(0f, viewer.ScrollY);

        Assert.True(form.FocusForAccessibility(viewer));
        HeadlessInput.PressKey(form, Key.End);
        form.UpdateLayout();

        Assert.True(viewer.ScrollY > 0);
    }

    // ===== blocks =====

    [Fact]
    public void ListsGetBulletsNumbersAndTaskBoxes()
    {
        (_, MarkdownViewer viewer) = Create("- a\n  - b\n\n3. c\n4. d\n\n- [x] done\n- [ ] todo");

        MarkdownLayout layout = viewer.Layout!;

        Assert.Single(layout.Boxes, b => b.Kind == MarkdownBoxKind.Disc);
        Assert.Single(layout.Boxes, b => b.Kind == MarkdownBoxKind.Circle);

        Assert.Contains("3.", Texts(viewer));
        Assert.Contains("4.", Texts(viewer));

        Assert.Equal([true, false], layout.Boxes.Where(b => b.Kind == MarkdownBoxKind.Check).Select(b => b.IsChecked));
    }

    [Fact]
    public void AListMarkerStandsAtItsItemsFirstLine()
    {
        (_, MarkdownViewer viewer) = Create("- one\n- two");

        MarkdownLayout layout = viewer.Layout!;
        MarkdownBox[] bullets = [.. layout.Boxes.Where(b => b.Kind == MarkdownBoxKind.Disc)];
        MarkdownLine two = layout.Lines.Single(l => l.Runs[0].Text == "two");

        float bulletCenter = bullets[1].Bounds.Y + bullets[1].Bounds.Height / 2f;

        Assert.Equal(two.Bounds.Y + two.Bounds.Height / 2f, bulletCenter, 0.01f);

        // the text starts past the marker column
        Assert.True(two.Bounds.X > bullets[1].Bounds.X + bullets[1].Bounds.Width);
    }

    [Fact]
    public void TablesSizeTheirColumnsByTheirContent()
    {
        (_, MarkdownViewer viewer) = Create("| A | Long header |\n|---|---:|\n| 1 | 2 |", width: 400);

        MarkdownLine header = viewer.Layout!.Lines.Single(l => l.Runs[0].Text == "Long header");
        MarkdownLine one = viewer.Layout.Lines.Single(l => l.Runs[0].Text == "1");
        MarkdownLine two = viewer.Layout.Lines.Single(l => l.Runs[0].Text == "2");

        // the first column is narrow, the second as wide as its header
        Assert.True(header.Bounds.X > one.Bounds.X);
        Assert.Equal(header.Bounds.X, two.Bounds.X, 0.01f);

        Assert.Equal(HorizontalContentAlignment.Right, two.Align);
        Assert.Equal(FontWeight.Bold, header.Runs[0].Font!.Weight);
    }

    [Fact]
    public void APictureAloneInAParagraphIsShownFittedToTheWidth()
    {
        var picture = new Image(400, 100, new byte[400 * 100 * 4]);

        var viewer = new MarkdownViewer
        {
            Font = new Font("Test", 10),
            ImageResolver = url => url == "wide.png" ? picture : null,
            Markdown = "![Wide](wide.png)\n\n![Missing](none.png)",
        };

        var form = new Form { Size = new Size(200, 300), Content = viewer };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        MarkdownBox image = Assert.Single(viewer.Layout!.Boxes, b => b.Kind == MarkdownBoxKind.Image);

        Assert.Equal(new Size(200, 50), image.Bounds.Size);

        // one that can't be had reads as its alt text
        Assert.Contains("Missing", Texts(viewer));
    }

    [Fact]
    public void RightToLeftAlignsTheTextRight()
    {
        (Form form, MarkdownViewer viewer) = Create("text");

        viewer.FlowDirection = FlowDirection.RightToLeft;
        form.UpdateLayout();

        Assert.Equal(HorizontalContentAlignment.Right, Assert.Single(viewer.Layout!.Lines).Align);
    }

    [Fact]
    public void AChangedColorLaysTheDocumentOutAgain()
    {
        (Form form, MarkdownViewer viewer) = Create("[link](https://x)");

        var red = new Color(255, 255, 0, 0);
        viewer.LinkColor = red;
        form.UpdateLayout();

        Assert.Equal(red, Assert.Single(viewer.Layout!.Lines).Runs[0].Color);
    }

    // ===== links =====

    [Fact]
    public void AClickOnALinkReportsIt()
    {
        (Form form, MarkdownViewer viewer) = Create("go to [the site](https://zf.dev \"ZF\") now");

        MarkdownLinkEventArgs? clicked = null;
        viewer.LinkClicked += (_, e) => clicked = e;

        Rectangle link = Assert.Single(Assert.Single(viewer.Layout!.Links).Rects);

        // "go to " is six characters before it, "the site" eight long
        Assert.Equal(6 * Char, link.X);
        Assert.Equal(8 * Char, link.Width);

        Point center = Center(viewer, link);
        HeadlessInput.Click(form, center.X, center.Y);

        Assert.Equal("https://zf.dev", clicked!.Url);
        Assert.Equal("ZF", clicked.Title);
        Assert.Equal("the site", clicked.Text);
    }

    [Fact]
    public void AFragmentLinkScrollsToItsHeading()
    {
        // filler after the heading too: at the very end the scroll would stop short of it
        string filler = string.Join("\n\n", Enumerable.Range(0, 30).Select(i => $"Filler {i}"));
        string markdown = "[Down](#the-end)\n\n" + filler + "\n\n## The End\n\n" + filler;

        (Form form, MarkdownViewer viewer) = Create(markdown);

        Point center = Center(viewer, viewer.Layout!.Links[0].Rects[0]);
        HeadlessInput.Click(form, center.X, center.Y);
        form.UpdateLayout();

        float heading = viewer.Layout.Anchors["the-end"];

        Assert.True(viewer.ScrollY > 0);
        Assert.Equal(heading, viewer.ScrollY, 0.5f);
    }

    [Fact]
    public void AHandledFragmentLinkIsLeftToTheApplication()
    {
        (Form form, MarkdownViewer viewer) = Create("[Down](#x)\n\n" + string.Join("\n\n", Enumerable.Range(0, 30).Select(i => $"P {i}")) + "\n\n# X");

        viewer.LinkClicked += (_, e) => e.Handled = true;

        Point center = Center(viewer, viewer.Layout!.Links[0].Rects[0]);
        HeadlessInput.Click(form, center.X, center.Y);

        Assert.Equal(0f, viewer.ScrollY);
    }

    [Fact]
    public void TabGoesThroughTheLinksThenOnOut()
    {
        var after = new Button { Text = "After" };
        (Form form, MarkdownViewer viewer) = Create("[one](1) and [two](2)", after: after, height: 100);

        string? followed = null;
        viewer.LinkClicked += (_, e) => followed = e.Url;

        Assert.True(form.FocusForAccessibility(viewer));

        HeadlessInput.PressKey(form, Key.Tab);
        Assert.Equal(0, viewer.FocusedLink);

        HeadlessInput.PressKey(form, Key.Tab);
        Assert.Equal(1, viewer.FocusedLink);

        HeadlessInput.PressKey(form, Key.Enter);
        Assert.Equal("2", followed);

        HeadlessInput.PressKey(form, Key.Tab, KeyModifiers.Shift);
        Assert.Equal(0, viewer.FocusedLink);

        HeadlessInput.PressKey(form, Key.Tab);
        HeadlessInput.PressKey(form, Key.Tab);

        Assert.True(after.IsFocused);
    }

    [Fact]
    public void ALinkThatWrapsIsInTwoPlaces()
    {
        (_, MarkdownViewer viewer) = Create("xxxxxxxxxxxxxxxxxxxxxxxxxxx [one two three](u)");

        Assert.Equal(2, Assert.Single(viewer.Layout!.Links).Rects.Count);
    }

    // ===== accessibility =====

    [Fact]
    public void TheReaderGetsTheDocumentsStructure()
    {
        (_, MarkdownViewer viewer) = Create("# Title\n\nSee [docs](d).\n\n- [x] done\n\n| H |\n|---|\n| v |", width: 400);

        string? followed = null;
        viewer.LinkClicked += (_, e) => followed = e.Url;

        AccessibilityPeer peer = viewer.GetAccessibilityPeer()!;
        IReadOnlyList<AccessibilityPeer> blocks = peer.Children;

        Assert.Equal(
            [AccessibilityRole.Heading, AccessibilityRole.Text, AccessibilityRole.List, AccessibilityRole.Table],
            blocks.Select(b => b.Role));

        Assert.Equal("Title", blocks[0].Name);
        Assert.Equal(1, blocks[0].HeadingLevel);

        AccessibilityPeer link = Assert.Single(blocks[1].Children);
        Assert.Equal(AccessibilityRole.Link, link.Role);
        Assert.Equal("docs", link.Name);
        Assert.True(link.Invoke());
        Assert.Equal("d", followed);

        AccessibilityPeer item = Assert.Single(blocks[2].Children);
        Assert.True(item.States.HasFlag(AccessibilityStates.Checked));

        AccessibilityPeer header = blocks[3].Children[0].Children[0];
        Assert.Equal(AccessibilityRole.ColumnHeader, header.Role);
        Assert.Equal("v", blocks[3].Children[1].Children[0].Name);
    }
}