using Xunit;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>0.17.0: a multi-line text box scrolls with the wheel and its scroll bars,
/// and stays scrolled — the caret is followed only when it moves.</summary>
[Collection("Platform")]
public class TextBoxScrollingTests
{
    private sealed class ThumbRecorder(Color thumb) : HeadlessGraphics
    {
        public int Thumbs { get; private set; }

        public override void FillRoundRectangle(Rectangle rect, CornerRadius radius, Color color)
        {
            if (color == thumb) Thumbs++;
            base.FillRoundRectangle(rect, radius, color);
        }
    }

    private static string Lines(int count, string prefix = "line") =>
        string.Join("\n", Enumerable.Range(1, count).Select(i => $"{prefix} {i}"));

    private static (Form Form, TextBox Box) Show(string text, bool multiline = true)
    {
        // Shift held turns the wheel sideways, and the keyboard state is a snapshot
        // the tests share: a test before may have left it held
        Input.Keyboard.Keyboard.Reset();

        var box = new TextBox
        {
            IsMultiline = multiline,
            Text = text,
            Font = Font.Default.WithSize(10),
            Size = new Size(200, 100),
        };

        var form = new Form { Size = new Size(400, 300), Content = box };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();
        Draw(box);

        return (form, box);
    }

    private static int Draw(TextBox box)
    {
        var g = new ThumbRecorder(box.ScrollThumbColor);
        box.Draw(g);
        return g.Thumbs;
    }

    private static Point Middle(TextBox box)
    {
        Point at = box.GetAbsolutePosition();
        return new Point(at.X + box.ActualSize.Width / 2, at.Y + box.ActualSize.Height / 2);
    }

    [Fact]
    public void TheWheelScrollsALongTextAndItStaysScrolled()
    {
        (Form form, TextBox box) = Show(Lines(50));

        form.OnMouseWheel(Middle(box), -120);
        float scrolled = box.VerticalOffset;

        // a frame used to bring the field back to the caret at the first line
        Draw(box);

        Assert.True(scrolled > 0);
        Assert.Equal(scrolled, box.VerticalOffset, 0.01f);
    }

    [Fact]
    public void AtTheEndTheWheelGoesOnToThePanelAround()
    {
        var box = new TextBox
        {
            IsMultiline = true,
            Text = Lines(50),
            Font = Font.Default.WithSize(10),
            Size = new Size(200, 100),
        };

        // the panel is shorter than what it holds: it scrolls too
        var panel = new StackPanel
        {
            OverflowY = Overflow.Auto,
            Size = new Size(300, 150),
            Children = { box, new Panel { Size = new Size(200, 400) } },
        };

        Input.Keyboard.Keyboard.Reset();

        var form = new Form { Size = new Size(400, 300), Content = panel };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();
        Draw(box);

        // the field takes the wheel while it can scroll, and only then the panel does
        form.OnMouseWheel(Middle(box), -120);
        Assert.True(box.VerticalOffset > 0);
        Assert.Equal(0, panel.ScrollY);

        box.ScrollTo(0, float.MaxValue);
        form.OnMouseWheel(Middle(box), -120);

        Assert.True(panel.ScrollY > 0);
    }

    [Fact]
    public void TypingBringsTheCaretBack()
    {
        (Form form, TextBox box) = Show(Lines(50));
        form.FocusElement(box);

        form.OnMouseWheel(Middle(box), -360);
        Draw(box);
        Assert.True(box.VerticalOffset > 0);

        // the caret is at the start: a character typed there shows where it goes
        HeadlessInput.TypeText(form, "x");
        Draw(box);

        Assert.Equal(0, box.VerticalOffset);
    }

    [Fact]
    public void ALongTextShowsTheVerticalBarAndAShortOneDoesNot()
    {
        (_, TextBox longBox) = Show(Lines(50));
        (_, TextBox shortBox) = Show(Lines(2));

        Assert.Equal(1, Draw(longBox));
        Assert.Equal(0, Draw(shortBox));
    }

    [Fact]
    public void AHiddenBarStillLetsTheWheelScroll()
    {
        (Form form, TextBox box) = Show(Lines(50));
        box.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
        form.UpdateLayout();

        Assert.Equal(0, Draw(box));

        form.OnMouseWheel(Middle(box), -120);
        Assert.True(box.VerticalOffset > 0);
    }

    [Fact]
    public void ALongLineShowsTheHorizontalBarUnlessWrapped()
    {
        string wide = new('w', 200);

        (Form form, TextBox box) = Show(wide);
        Assert.Equal(1, Draw(box));

        box.WordWrap = true;
        form.UpdateLayout();
        Assert.Equal(0, Draw(box));
    }

    [Fact]
    public void APressOnTheTrackBelowTheThumbPagesDown()
    {
        (Form form, TextBox box) = Show(Lines(50));

        Point at = box.GetAbsolutePosition();
        Rectangle content = box.ContentBounds;

        // the bottom of the vertical bar: the thumb is at its top
        var track = new Point(at.X + content.X + content.Width - 5, at.Y + content.Y + content.Height - 5);

        form.OnPointerDown(track);
        form.OnPointerUp(track);

        Assert.True(box.VerticalOffset > 0);
        Assert.Equal(0, box.CaretIndex);   // a press on the bar is not a press on the text
    }

    [Fact]
    public void ASingleLineFieldDoesNotTakeTheWheel()
    {
        (Form form, TextBox box) = Show(new string('w', 200), multiline: false);

        form.OnMouseWheel(Middle(box), -120);

        Assert.Equal(0, box.VerticalOffset);
        Assert.Equal(0, Draw(box));
    }

    [Fact]
    public void EditingOneLineOfAWrappedTextLaysItOutAsAFreshField()
    {
        string text = string.Join("\n", Enumerable.Range(1, 30).Select(i => $"word {i} and a few more words to wrap"));

        (Form form, TextBox edited) = Show(text);
        edited.WordWrap = true;
        form.UpdateLayout();
        Draw(edited);

        // one line in the middle grows: the others keep their cached breaks
        edited.Text = text.Replace("word 15 and", "word 15 and many, many more words than before and");
        form.UpdateLayout();
        Draw(edited);

        (Form freshForm, TextBox fresh) = Show(edited.Text);
        fresh.WordWrap = true;
        freshForm.UpdateLayout();
        Draw(fresh);

        Assert.Equal(fresh.DesiredSize, edited.DesiredSize);

        // and the scroll range is the same: the rows are the same rows
        edited.ScrollTo(0, float.MaxValue);
        fresh.ScrollTo(0, float.MaxValue);
        Assert.Equal(fresh.VerticalOffset, edited.VerticalOffset, 0.01f);
    }
}
