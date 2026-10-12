using Xunit;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.UnitTests;

/// <summary>Input methods: the text they compose is shown underlined at the caret and
/// isn't in the text until committed; the field tells the window where its caret is.</summary>
[Collection("Platform")]
public class TextCompositionTests
{
    private sealed class Recorder : HeadlessGraphics
    {
        public List<string> Lines { get; } = [];

        public List<Rectangle> Fills { get; } = [];

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
            Lines.Add(text);
            base.DrawTextLine(text, rect, color, font, hAlign, vAlign);
        }
    }

    private static (Form Form, HeadlessWindow Window, TextBox Box, Button Other) Show(string text = "")
    {
        var box = new TextBox { Text = text, Font = Font.Default.WithSize(10), Size = new Size(300, 30) };
        var other = new Button { Text = "Other" };

        var form = new Form
        {
            Size = new Size(400, 200),
            Content = new StackPanel { Orientation = Orientation.Vertical, Children = { box, other } },
        };

        var window = (HeadlessWindow)new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        Point abs = box.GetAbsolutePosition();
        HeadlessInput.Click(form, abs.X + box.ContentBounds.X + 1, abs.Y + box.ContentBounds.Y + 5);

        return (form, window, box, other);
    }

    private static Recorder Render(Form form)
    {
        var recorder = new Recorder();
        form.UpdateLayout();
        ElementTreeRenderer.Draw(form.Content!, recorder);
        return recorder;
    }

    [Fact]
    public void TheCompositionIsShownButNotInTheText()
    {
        var (form, _, box, _) = Show("ab");
        box.Select(1, 0);

        HeadlessInput.Compose(form, "ni");

        Assert.Equal("ab", box.Text);
        Assert.Contains("anib", Render(form).Lines);
    }

    [Fact]
    public void ACommitGoesInAsTyped()
    {
        var (form, _, box, _) = Show("ab");
        box.Select(1, 0);

        HeadlessInput.Compose(form, "ni");
        HeadlessInput.CommitComposition(form, "你");

        Assert.Equal("a你b", box.Text);
        Assert.Equal(2, box.CaretIndex);
        Assert.Contains("a你b", Render(form).Lines);
    }

    [Fact]
    public void TheCompositionIsUnderlined()
    {
        var (form, _, box, _) = Show();
        HeadlessInput.Compose(form, "ka");

        Rectangle content = box.ContentBounds;

        // a thin line under the two letters: 12 pixels at size 10
        Assert.Contains(Render(form).Fills, r => Math.Abs(r.X - content.X) < 0.5f && Math.Abs(r.Width - 12) < 0.5f && r.Height <= 2);
    }

    [Fact]
    public void ACancelledCompositionLeavesNothing()
    {
        var (form, _, box, _) = Show("x");

        HeadlessInput.Compose(form, "ka");
        HeadlessInput.Compose(form, string.Empty);

        Assert.Equal("x", box.Text);
        Assert.Contains("x", Render(form).Lines);
    }

    [Fact]
    public void TheCompositionTakesTheSelectionsPlace()
    {
        var (form, _, box, _) = Show("abc");
        box.SelectAll();

        HeadlessInput.Compose(form, "k");

        Assert.Equal(string.Empty, box.Text);
    }

    [Fact]
    public void KeysBelongToTheInputMethodWhileItComposes()
    {
        var (form, _, box, _) = Show("abc");
        box.Select(1, 0);

        HeadlessInput.Compose(form, "ka");
        HeadlessInput.PressKey(form, Key.Backspace);
        HeadlessInput.PressKey(form, Key.Left);

        Assert.Equal("abc", box.Text);
        Assert.Equal(1, box.CaretIndex);
    }

    [Fact]
    public void APasswordFieldTakesOnlyCommittedText()
    {
        var (form, _, box, _) = Show();
        box.PasswordChar = '*';

        HeadlessInput.Compose(form, "ka");
        Assert.DoesNotContain(Render(form).Lines, l => l.Length > 0);

        HeadlessInput.CommitComposition(form, "ka");
        Assert.Equal("ka", box.Text);
    }

    [Fact]
    public void TheWindowLearnsWhereTheCaretIs()
    {
        var (form, window, box, other) = Show("abc");
        box.Select(3, 0);
        Render(form);

        Point abs = box.GetAbsolutePosition();
        Assert.NotNull(window.TextInputArea);
        Assert.Equal(abs.X + box.ContentBounds.X + 18, window.TextInputArea!.Value.X, 0.5f);

        // focus on a button: no text is being typed anywhere
        HeadlessInput.PressKey(form, Key.Tab);
        Assert.True(other.IsFocused);
        Assert.Null(window.TextInputArea);
    }

    [Fact]
    public void AClickDropsTheComposition()
    {
        var (form, _, box, _) = Show("abc");

        HeadlessInput.Compose(form, "ka");

        Point abs = box.GetAbsolutePosition();
        HeadlessInput.Click(form, abs.X + box.ContentBounds.X + 1, abs.Y + box.ContentBounds.Y + 5);

        Assert.Equal("abc", box.Text);
        Assert.Contains("abc", Render(form).Lines);
    }
}
