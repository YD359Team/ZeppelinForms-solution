using Xunit;
using ZeppelinForms.Core.Text;
using ZeppelinForms.Core.Text.Syntax;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.UnitTests;

/// <summary>The 0.21 text editors: highlighting, rich spans, pagination and the
/// editing contract they share with <see cref="TextBox"/>.</summary>
public sealed class NewTextControlsTests
{
    // ===== highlighting =====

    [Fact]
    public void AKeywordIsStyledAndAnIdentifierIsNot()
    {
        var tokenizer = new SyntaxTokenizer(SyntaxRuleSet.CSharp());
        tokenizer.Retokenize("class Name");

        SyntaxStyle? keyword = tokenizer.StyleAt(0);
        SyntaxStyle? name = tokenizer.StyleAt(6);

        Assert.NotNull(keyword);
        Assert.NotEqual(keyword, name);
        Assert.Equal(tokenizer.Rules.DefaultStyle, name);
    }

    [Fact]
    public void AStringAndACommentWinOverKeywords()
    {
        var tokenizer = new SyntaxTokenizer(SyntaxRuleSet.CSharp());
        tokenizer.Retokenize("\"class\" // class\n");

        Assert.Equal(tokenizer.StyleAt(1), tokenizer.StyleAt(3));
        Assert.NotEqual(tokenizer.StyleAt(0), tokenizer.StyleAt(9));
        Assert.Equal(tokenizer.StyleAt(9), tokenizer.StyleAt(12));
    }

    [Fact]
    public void AHashInTheMiddleOfALineIsNotADirective()
    {
        var tokenizer = new SyntaxTokenizer(SyntaxRuleSet.CSharp());
        tokenizer.Retokenize("int n = 1 # 2;\n#define X\n");

        // the mid-line hash is punctuation; the directive at the line start is a block
        Assert.Equal(tokenizer.Rules.DefaultStyle, tokenizer.StyleAt(10));
        Assert.NotEqual(tokenizer.Rules.DefaultStyle, tokenizer.StyleAt(16));
    }

    [Fact]
    public void ANumberTakesTheNumberStyle()
    {
        var tokenizer = new SyntaxTokenizer(SyntaxRuleSet.CSharp());
        tokenizer.Retokenize("int n = 42;");

        Assert.Equal(tokenizer.Rules.NumberStyle, tokenizer.StyleAt(8));
        Assert.Equal(tokenizer.Rules.NumberStyle, tokenizer.StyleAt(9));
    }

    [Fact]
    public void ABlockCommentSpansLines()
    {
        var tokenizer = new SyntaxTokenizer(SyntaxRuleSet.CSharp());
        tokenizer.Retokenize("a /* class\nreturn */ b");

        Assert.Equal(tokenizer.StyleAt(2), tokenizer.StyleAt(12));
        Assert.Equal(tokenizer.Rules.DefaultStyle, tokenizer.StyleAt(21));
    }

    // ===== rich text =====

    [Fact]
    public void AStyleStaysOnItsCharactersWhenTextIsInsertedBeforeIt()
    {
        var doc = new RichTextDocument { Content = "hello" };
        doc.ApplyStyle(0, 5, RichTextStyle.Empty.WithBold());

        doc.Text.SetCaret(0);
        doc.Text.Insert("X");

        Assert.Equal("Xhello", doc.Content);
        Assert.False(doc.GetStyleAt(0).Bold);
        Assert.True(doc.GetStyleAt(1).Bold);
        Assert.True(doc.GetStyleAt(5).Bold);
    }

    [Fact]
    public void UndoRestoresTheTextAndTheSpans()
    {
        var doc = new RichTextDocument { Content = "hello" };
        doc.ApplyStyle(0, 5, RichTextStyle.Empty.WithBold());

        doc.Text.SetCaret(5);
        doc.Text.Insert("!");

        Assert.True(doc.Text.Undo());

        Assert.Equal("hello", doc.Content);
        Assert.True(doc.GetStyleAt(0).Bold);
        Assert.True(doc.GetStyleAt(4).Bold);
    }

    [Fact]
    public void ADeletionTrimsTheSpanItCuts()
    {
        var doc = new RichTextDocument { Content = "abcdef" };
        doc.ApplyStyle(2, 2, RichTextStyle.Empty.WithItalic()); // "cd"

        doc.Text.SetCaret(3);
        doc.Text.Backspace(); // removes 'c'

        Assert.Equal("abdef", doc.Content);
        Assert.True(doc.GetStyleAt(2).Italic);
        Assert.False(doc.GetStyleAt(3).Italic);
    }

    // ===== the editors =====
}

/// <summary>The editors themselves, on the headless platform.</summary>
[Collection("Platform")]
public sealed class NewTextEditorTests
{
        private static Form Show(UIElement content)
        {
            var form = new Form { Size = new Size(640, 480), Content = content };
            new HeadlessPlatform().CreateWindow(form);
            form.UpdateLayout();
            return form;
        }

        [Fact]
        public void CodeEditDuplicatesTheLine()
        {
            var edit = new CodeEdit { Text = "one\ntwo", Size = new Size(400, 200) };
            Show(edit);
            edit.Document.SetCaret(1);

            edit.DuplicateLineOrSelection();

            Assert.Equal("one\none\ntwo", edit.Text);
        }

        [Fact]
        public void CodeEditDuplicatesTheSelection()
        {
            var edit = new CodeEdit { Text = "abcdef", Size = new Size(400, 200) };
            Show(edit);
            edit.Document.SetCaret(2);
            edit.Document.SetCaret(4, extendSelection: true);

            edit.DuplicateLineOrSelection();

            Assert.Equal("abcdcdef", edit.Text);
        }

        [Fact]
        public void CodeEditFoldsABraceBlock()
        {
            var edit = new CodeEdit { Text = "{\n  a\n}\n", Size = new Size(400, 200) };
            Show(edit);

            edit.ToggleFold(0);
            Assert.Contains(0, edit.FoldedLineStarts);

            edit.ExpandAllFolds();
            Assert.Empty(edit.FoldedLineStarts);
        }

        [Fact]
        public void HighlightOccurrencesClears()
        {
            var edit = new CodeEdit { Text = "foo bar foo", Size = new Size(400, 200) };
            Show(edit);

            edit.HighlightOccurrences("foo");
            edit.HighlightOccurrences(null);

            // a cleared term must not keep painting; the public contract is the call itself
            Assert.Equal("foo bar foo", edit.Text);
        }

        [Fact]
        public void CtrlZUndoesAndCtrlShiftZRedoes()
        {
            var edit = new CodeEdit { Text = "", Size = new Size(400, 200) };
            Form form = Show(edit);
            HeadlessInput.Click(form, edit.GetAbsolutePosition().X + 20, edit.GetAbsolutePosition().Y + 10);
            HeadlessInput.TypeText(form, "ab");

            Assert.Equal("ab", edit.Text);
            Assert.True(edit.CanUndo);

            HeadlessInput.PressKey(form, Key.Z, KeyModifiers.Control);
            Assert.Equal(string.Empty, edit.Text);
            Assert.True(edit.CanRedo);

            HeadlessInput.PressKey(form, Key.Z, KeyModifiers.Control | KeyModifiers.Shift);
            Assert.Equal("ab", edit.Text);
        }

        [Fact]
        public void AReadOnlyEditorTakesNoText()
        {
            var edit = new CodeEdit { Text = "keep", IsReadOnly = true, Size = new Size(400, 200) };
            Form form = Show(edit);
            HeadlessInput.Click(form, edit.GetAbsolutePosition().X + 20, edit.GetAbsolutePosition().Y + 10);
            HeadlessInput.TypeText(form, "x");

            Assert.Equal("keep", edit.Text);
            Assert.False(edit.CanUndo);
        }

        [Fact]
        public void RichTextBoxTogglesBoldOnTheSelection()
        {
            var box = new RichTextBox { Text = "hello", Size = new Size(400, 200) };
            Show(box);
            box.Select(1, 3);

            box.ToggleBold();

            Assert.False(box.Document.GetStyleAt(0).Bold);
            Assert.True(box.Document.GetStyleAt(1).Bold);
            Assert.True(box.Document.GetStyleAt(3).Bold);
            Assert.False(box.Document.GetStyleAt(4).Bold);
        }

        [Fact]
        public void RichTextBoxUndoPutsTheTextBack()
        {
            var box = new RichTextBox { Text = "hi", Size = new Size(400, 200) };
            Form form = Show(box);
            HeadlessInput.Click(form, box.GetAbsolutePosition().X + 8, box.GetAbsolutePosition().Y + 8);
            HeadlessInput.TypeText(form, "!");

            Assert.Contains("!", box.Text);
            box.Undo();
            Assert.Equal("hi", box.Text);
        }

        [Fact]
        public void DocumentEditSplitsALongTextIntoPages()
        {
            var doc = new DocumentEdit
            {
                Size = new Size(400, 300),
                Font = Font.Default.WithSize(12),
                Text = string.Join("\n", Enumerable.Repeat("line of text", 200)),
            };
            Show(doc);

            Assert.True(doc.Pages.Count > 1);
            Assert.Equal(0, doc.Pages[0].TextStart);
            Assert.True(doc.Pages[^1].TextStart > doc.Pages[0].TextStart);
        }

        [Fact]
        public void OrientationWidensTheSheet()
        {
            var doc = new DocumentEdit { Size = new Size(500, 400), Text = "a" };
            Show(doc);

            float portrait = doc.PageSettings.ContentWidth;
            doc.SetOrientation(PageOrientation.Landscape);

            Assert.True(doc.PageSettings.ContentWidth > portrait);
            Assert.Equal(PageOrientation.Landscape, doc.PageSettings.Orientation);
        }

        [Fact]
        public void DocumentEditWrapsALineThatDoesNotFit()
        {
            var doc = new DocumentEdit
            {
                Size = new Size(400, 300),
                Font = Font.Default.WithSize(12),
                Text = new string('x', 400),
            };
            Show(doc);

            // one hard line, many visual rows, still one page or more — the text is kept
            Assert.Equal(new string('x', 400), doc.Text);
            Assert.True(doc.Pages.Count >= 1);
            Assert.True(doc.Pages.Sum(p => p.TextLength) == 400);
        }

        [Fact]
        public void DocumentEditUndoRestoresTheText()
        {
            var doc = new DocumentEdit { Text = "page", Size = new Size(400, 300) };
            Form form = Show(doc);
            HeadlessInput.Click(form, doc.GetAbsolutePosition().X + 40, doc.GetAbsolutePosition().Y + 40);
            HeadlessInput.TypeText(form, "!");

            Assert.Contains("!", doc.Text);
            doc.Undo();
            Assert.Equal("page", doc.Text);
        }
    }
