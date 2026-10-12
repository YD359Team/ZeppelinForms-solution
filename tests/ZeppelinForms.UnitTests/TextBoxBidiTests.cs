using Xunit;
using ZeppelinForms.Core.Text;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.UnitTests;

/// <summary>A text box in mixed and right-to-left text: carets, clicks, selections and
/// arrows go by the row as it stands on the screen. The headless measurer gives every
/// character 6 pixels at size 10.</summary>
[Collection("Platform")]
public class TextBoxBidiTests
{
    private const string Hebrew = "אבג";

    private sealed class Recorder : HeadlessGraphics
    {
        public List<Rectangle> Fills { get; } = [];

        public List<(string Text, Rectangle Rect)> Lines { get; } = [];

        public override void FillRectangle(Rectangle rect, Color color)
        {
            Fills.Add(rect);
            base.FillRectangle(rect, color);
        }

        public override void DrawTextLine(
            string text, Rectangle rect, Color color, Font font,
            HorizontalContentAlignment hAlign = HorizontalContentAlignment.Left,
            VerticalContentAlignment vAlign = VerticalContentAlignment.Center)
        {
            Lines.Add((text, rect));
            base.DrawTextLine(text, rect, color, font, hAlign, vAlign);
        }
    }

    private static (Form Form, TextBox Box) Show(string text, FlowDirection direction = FlowDirection.LeftToRight)
    {
        var box = new TextBox
        {
            Text = text,
            Font = Font.Default.WithSize(10),
            FlowDirection = direction,
            Size = new Size(300, 30),
        };

        var form = new Form { Size = new Size(400, 200), Content = box };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        // a click focuses the field
        var (x, y) = At(box, 1);
        HeadlessInput.Click(form, x, y);

        return (form, box);
    }

    /// <summary>A point of the form at an x of the box's text area.</summary>
    private static (float X, float Y) At(TextBox box, float x)
    {
        Point abs = box.GetAbsolutePosition();
        Rectangle content = box.ContentBounds;

        return (abs.X + content.X + x, abs.Y + content.Y + content.Height / 2f);
    }

    [Fact]
    public void TheArrowsCrossAHebrewWordAsItIsDrawn()
    {
        var (form, box) = Show($"abc {Hebrew}");

        HeadlessInput.PressKey(form, Key.Home);

        var visited = new List<int>();

        for (int i = 0; i < 8; i++)
        {
            HeadlessInput.PressKey(form, Key.Right);
            visited.Add(box.CaretIndex);
        }

        // on the screen: a b c ␠ | then the Hebrew word from its left end: G B A
        Assert.Equal([1, 2, 3, 7, 6, 5, 4, 4], visited);
    }

    [Fact]
    public void RightGoesBackInARightToLeftField()
    {
        var (form, box) = Show(Hebrew, FlowDirection.RightToLeft);

        box.Select(3, 0);
        HeadlessInput.PressKey(form, Key.Right);

        Assert.Equal(2, box.CaretIndex);

        HeadlessInput.PressKey(form, Key.Left);
        Assert.Equal(3, box.CaretIndex);
    }

    [Fact]
    public void AClickOnAHebrewLetterPicksItsNearerEdge()
    {
        var (form, box) = Show($"abc {Hebrew}");

        // the alef stands at 36–42: its right half is before it in the text
        var (x, y) = At(box, 41);
        HeadlessInput.Click(form, x, y);
        Assert.Equal(4, box.CaretIndex);

        (x, y) = At(box, 37);
        HeadlessInput.Click(form, x, y);
        Assert.Equal(5, box.CaretIndex);
    }

    [Fact]
    public void ASelectionAcrossDirectionsIsDrawnInPieces()
    {
        var (form, box) = Show($"abc {Hebrew}");
        box.Select(2, 3);

        var recorder = new Recorder();
        ElementTreeRenderer.Draw(form.Content!, recorder);

        Rectangle content = box.ContentBounds;

        // "c␠" at the left and the alef at the right of the Hebrew word
        Assert.Contains(recorder.Fills, r => Math.Abs(r.X - (content.X + 12)) < 0.5f && Math.Abs(r.Width - 12) < 0.5f);
        Assert.Contains(recorder.Fills, r => Math.Abs(r.X - (content.X + 36)) < 0.5f && Math.Abs(r.Width - 6) < 0.5f);
    }

    [Fact]
    public void ARightToLeftFieldStartsAtTheRight()
    {
        var (form, box) = Show(Hebrew, FlowDirection.RightToLeft);

        var recorder = new Recorder();
        ElementTreeRenderer.Draw(form.Content!, recorder);

        Rectangle content = box.ContentBounds;
        (string _, Rectangle rect) = Assert.Single(recorder.Lines);

        Assert.Equal(content.X + content.Width - 18, rect.X, 0.5f);
    }

    [Fact]
    public void TheAlignmentIsHonoured()
    {
        var (form, box) = Show("abc");
        box.HorizontalContentAlign = HorizontalContentAlignment.Right;
        form.UpdateLayout();

        var recorder = new Recorder();
        ElementTreeRenderer.Draw(form.Content!, recorder);

        Rectangle content = box.ContentBounds;
        (string _, Rectangle rect) = Assert.Single(recorder.Lines);

        Assert.Equal(content.X + content.Width - 18, rect.X, 0.5f);
    }

    [Fact]
    public void ALatinFieldMovesAsItAlwaysDid()
    {
        var (form, box) = Show("hello");

        HeadlessInput.PressKey(form, Key.Home);
        HeadlessInput.PressKey(form, Key.Right);
        HeadlessInput.PressKey(form, Key.Right, KeyModifiers.Shift);

        Assert.Equal(2, box.CaretIndex);
        Assert.Equal(1, box.SelectionStart);
        Assert.Equal(1, box.SelectionLength);
    }
}
