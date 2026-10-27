using Xunit;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>How a label lays its text out: rows of the font's line height, broken at
/// the edge with <see cref="Label.TextWrapping"/>, shortened with
/// <see cref="Label.TextTrimming"/>, capped by <see cref="Label.MaxLines"/>; its
/// alignments are styled properties.</summary>
/// <remarks>The headless measurer gives every character 0.6 of the font size and a
/// line 1.2 of it: at the default 14 px, 8.4 px a character and 16.8 px a row.</remarks>
[Collection("Platform")]
public class LabelTextLayoutTests
{
    private const float CharWidth = 14 * 0.6f;
    private const float RowHeight = 14 * 1.2f;

    /// <summary>Records the captions drawn and where.</summary>
    private sealed class Recorder : HeadlessGraphics
    {
        public List<(string Text, Rectangle Rect)> Texts { get; } = [];

        public override void DrawText(string text, Rectangle rect, Color color, Font font,
            HorizontalContentAlignment hAlign = HorizontalContentAlignment.Center,
            VerticalContentAlignment vAlign = VerticalContentAlignment.Center) =>
            Texts.Add((text, rect));
    }

    /// <summary>A label stretched across a column <paramref name="width"/> wide.</summary>
    private static (Form Form, Label Label) Show(Label label, float width = 100, float height = 400)
    {
        var column = new StackPanel { Orientation = Orientation.Vertical, CrossAxisAlignment = CrossAxisAlignment.Stretch };
        column.Children.Add(label);

        var form = new Form { Size = new Size(width, height), Content = column };

        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return (form, label);
    }

    private static List<string> Drawn(Form form)
    {
        var recorder = new Recorder();
        ElementTreeRenderer.Draw(form.Content!, recorder);
        return [.. recorder.Texts.Select(t => t.Text)];
    }

    private static List<(string Text, Rectangle Rect)> DrawnRects(Form form)
    {
        var recorder = new Recorder();
        ElementTreeRenderer.Draw(form.Content!, recorder);
        return recorder.Texts;
    }

    [Fact]
    public void ALineStaysOneRowByDefault()
    {
        var (form, label) = Show(new Label { Text = "one two three four" });

        Assert.Equal(RowHeight, label.DesiredSize.Height, 0.5f);
        Assert.Equal(["one two three four"], Drawn(form));
    }

    [Fact]
    public void WrapBreaksAfterSpaces()
    {
        // eleven characters fit a row of 100 px
        var (form, label) = Show(new Label { Text = "one two three four", TextWrapping = TextWrapping.Wrap });

        Assert.Equal(["one two", "three four"], Drawn(form));
        Assert.Equal(2 * RowHeight, label.ActualSize.Height, 0.5f);
    }

    [Fact]
    public void AWordWiderThanTheRowIsBrokenWhereItOverflows()
    {
        var (form, _) = Show(new Label { Text = "abcdefghijklmnopqrstuvwxyz", TextWrapping = TextWrapping.Wrap });

        Assert.Equal(["abcdefghijk", "lmnopqrstuv", "wxyz"], Drawn(form));
    }

    [Fact]
    public void EachLineOfTheTextWrapsOnItsOwn()
    {
        var (form, _) = Show(new Label { Text = "one two three\nfour", TextWrapping = TextWrapping.Wrap });

        Assert.Equal(["one two", "three", "four"], Drawn(form));
    }

    [Fact]
    public void CharacterEllipsisShortensARowThatDoesNotFit()
    {
        var (form, label) = Show(new Label { Text = "abcdefghijklmnopqrstuvwxyz", TextTrimming = TextTrimming.CharacterEllipsis });

        Assert.Equal(["abcdefghij…"], Drawn(form));
        Assert.True(label.DesiredSize.Width <= 100, $"width {label.DesiredSize.Width}");
    }

    [Fact]
    public void WordEllipsisShortensAtAWord()
    {
        var (form, _) = Show(new Label { Text = "one two three four", TextTrimming = TextTrimming.WordEllipsis });

        Assert.Equal(["one two…"], Drawn(form));
    }

    [Fact]
    public void MaxLinesKeepsTheFirstRowsAndMarksTheCut()
    {
        var (form, label) = Show(new Label
        {
            Text = "one two three four five six",
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 2,
        });

        List<string> rows = Drawn(form);

        Assert.Equal(2, rows.Count);
        Assert.Equal("one two", rows[0]);
        Assert.EndsWith("…", rows[1]);
        Assert.Equal(2 * RowHeight, label.ActualSize.Height, 0.5f);
    }

    [Fact]
    public void AWrappedLabelIsNoWiderThanItsRoom()
    {
        // at the start of a column a label takes its own width: the rows decide it
        var column = new StackPanel { Orientation = Orientation.Vertical, CrossAxisAlignment = CrossAxisAlignment.Start };
        var label = new Label { Text = "one two three four five six seven", TextWrapping = TextWrapping.Wrap };
        column.Children.Add(label);

        var form = new Form { Size = new Size(100, 400), Content = column };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        Assert.True(label.ActualSize.Width <= 100, $"width {label.ActualSize.Width}");

        // drawn at its own width it breaks where it was measured
        Assert.Equal(["one two", "three four", "five six", "seven"], Drawn(form));
        Assert.Equal(4 * RowHeight, label.ActualSize.Height, 0.5f);
    }

    [Fact]
    public void TheTextIsCenteredUpAndDownByDefault()
    {
        var (form, _) = Show(new Label { Text = "status", Size = new Size(float.NaN, 100) });

        Rectangle row = DrawnRects(form).Single().Rect;

        Assert.Equal((100 - RowHeight) / 2f, row.Y, 0.5f);
    }

    [Fact]
    public void AStyleSheetSetsTheAlignment()
    {
        var label = new Label { Text = "status", Size = new Size(float.NaN, 100) };
        label.Classes.Add("top");

        var (form, _) = Show(label);
        form.Styles.Add(StyleSheet.Parse("Label.top { VerticalContentAlign: Top; }", "test.zss"));
        form.UpdateLayout();

        Assert.Equal(VerticalContentAlignment.Top, label.VerticalContentAlign);
        Assert.Equal(0, DrawnRects(form).Single().Rect.Y, 0.5f);
    }

    [Fact]
    public void ChangingTheAlignmentRedraws()
    {
        var (form, label) = Show(new Label { Text = "status" });
        var window = (HeadlessWindow)form.PlatformWindow!;
        int before = window.InvalidateCount;

        label.HorizontalContentAlign = HorizontalContentAlignment.Right;

        Assert.True(window.InvalidateCount > before, "the change was not drawn");
    }

    [Fact]
    public void ARowIsAsTallAsTheFontsLine()
    {
        FontMetrics metrics = TextMeasurer.Current.MeasureFont(Font.Default);

        Assert.Equal(RowHeight, metrics.LineHeight, 0.01f);
        Assert.Equal(RowHeight, TextMeasurer.MeasureLine("Apply", Font.Default).Height, 0.01f);
        Assert.Equal(RowHeight, TextMeasurer.MeasureLine(string.Empty, Font.Default).Height, 0.01f);
    }

    [Fact]
    public void TheBaselineSitsByTheLineBox()
    {
        var metrics = new FontMetrics(16, 4);

        Assert.Equal(10 + 16, metrics.Baseline(10, 40, VerticalContentAlignment.Top), 0.01f);
        Assert.Equal(10 + 40 - 4, metrics.Baseline(10, 40, VerticalContentAlignment.Bottom), 0.01f);
        Assert.Equal(10 + 10 + 16, metrics.Baseline(10, 40, VerticalContentAlignment.Center), 0.01f);
    }
}
