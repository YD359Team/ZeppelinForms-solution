using Xunit;
using ZeppelinForms.Core.Text;

namespace ZeppelinForms.UnitTests;

/// <summary>The document's lines from its index of line starts: the same positions the
/// scan of the text and its split gave, before and after edits, undo and redo.</summary>
public sealed class TextDocumentLineIndexTests
{
    /// <summary>The position of an index as the scan of the text found it.</summary>
    private static (int Line, int Column) Scanned(string text, int index)
    {
        int line = 0, lineStart = 0;

        for (int i = 0; i < index && i < text.Length; i++)
            if (text[i] == '\n') { line++; lineStart = i + 1; }

        return (line, index - lineStart);
    }

    /// <summary>The index of a position as the split of the text found it.</summary>
    private static int Split(string text, int line, int column)
    {
        string[] lines = text.Split('\n');
        line = Math.Clamp(line, 0, lines.Length - 1);

        int index = 0;
        for (int i = 0; i < line; i++)
            index += lines[i].Length + 1;

        return index + Math.Clamp(column, 0, lines[line].Length);
    }

    /// <summary>Every index and every position of the text, each against what the scan
    /// and the split give.</summary>
    private static void AssertLikeTheText(TextDocument doc)
    {
        string text = doc.Text;
        int lines = text.Split('\n').Length;

        for (int index = 0; index <= text.Length; index++)
            Assert.Equal(Scanned(text, index), doc.ToPosition(index));

        // past the last line and the end of a line too: both are clamped
        for (int line = -1; line <= lines; line++)
            for (int column = -1; column <= 12; column++)
                Assert.Equal(Split(text, line, column), doc.FromPosition(line, column));
    }

    [Theory]
    [InlineData("")]
    [InlineData("one line")]
    [InlineData("\n")]
    [InlineData("\n\n\n")]
    [InlineData("first\nsecond\n\nfourth")]
    [InlineData("ends with a break\n")]
    [InlineData("\nstarts with a break")]
    public void PositionsAreThoseOfTheText(string text)
    {
        AssertLikeTheText(new TextDocument { IsMultiline = true, Text = text });
    }

    [Fact]
    public void EditsAtTheStartTheMiddleAndTheEndMoveTheLines()
    {
        var doc = new TextDocument { IsMultiline = true, Text = "alpha\nbeta\ngamma" };

        // at the start: a new first line
        doc.SetCaret(0);
        doc.Insert("zero\n");
        Assert.Equal("zero\nalpha\nbeta\ngamma", doc.Text);
        AssertLikeTheText(doc);

        // in the middle: two lines become one
        doc.SetCaret(doc.FromPosition(2, 0));
        doc.Backspace();
        Assert.Equal("zero\nalphabeta\ngamma", doc.Text);
        AssertLikeTheText(doc);

        // at the end: a selection across lines replaced by a break
        doc.SetCaret(doc.FromPosition(1, 5));
        doc.SetCaret(doc.Text.Length, extendSelection: true);
        doc.Insert("\n");
        Assert.Equal("zero\nalpha\n", doc.Text);
        AssertLikeTheText(doc);
    }

    [Fact]
    public void UndoAndRedoGiveTheLinesBack()
    {
        var doc = new TextDocument { IsMultiline = true, Text = "a\nb\nc", MergeWindow = TimeSpan.Zero };

        doc.SetCaret(doc.FromPosition(1, 1));
        doc.Insert("\nmore\nlines");
        string edited = doc.Text;

        doc.SetCaret(0);
        doc.Delete();
        doc.Delete();

        Assert.True(doc.Undo());
        Assert.True(doc.Undo());
        Assert.Equal(edited, doc.Text);
        AssertLikeTheText(doc);

        Assert.True(doc.Undo());
        Assert.Equal("a\nb\nc", doc.Text);
        AssertLikeTheText(doc);

        Assert.True(doc.Redo());
        Assert.Equal(edited, doc.Text);
        AssertLikeTheText(doc);
    }

    [Fact]
    public void LineMovementAndSelectionFollowTheIndex()
    {
        var doc = new TextDocument { IsMultiline = true, Text = "long first line\nshort\n\nlast one" };

        doc.SetCaret(doc.FromPosition(1, 3));

        doc.MoveToLineEnd(extend: false);
        Assert.Equal(doc.FromPosition(1, 5), doc.CaretIndex);

        doc.MoveToLineStart(extend: false);
        Assert.Equal(doc.FromPosition(1, 0), doc.CaretIndex);

        doc.SelectLine();
        Assert.Equal("short", doc.SelectedText);

        // the empty line: nothing selected, the caret where it starts
        doc.SetCaret(doc.FromPosition(2, 0));
        doc.SelectLine();
        Assert.Equal(0, doc.SelectionLength);

        // down from the end of the first line: the column kept past the short ones
        doc.SetCaret(doc.FromPosition(0, 9));
        doc.MoveVertical(1, extend: false);
        Assert.Equal((1, 5), doc.ToPosition(doc.CaretIndex));
        doc.MoveVertical(1, extend: false);
        Assert.Equal((2, 0), doc.ToPosition(doc.CaretIndex));
        doc.MoveVertical(1, extend: false);
        Assert.Equal((3, 8), doc.ToPosition(doc.CaretIndex));
    }

    [Fact]
    public void ManyRandomEditsKeepThePositionsOfTheText()
    {
        var random = new Random(18_1);
        var doc = new TextDocument { IsMultiline = true, Text = "start\nof\nthe text", MergeWindow = TimeSpan.Zero };
        string[] pieces = ["\n", "x", "ab\ncd", "\n\n", "line"];

        for (int step = 0; step < 300; step++)
        {
            int length = doc.Text.Length;

            switch (random.Next(5))
            {
                case 0 or 1:
                    doc.SetCaret(random.Next(length + 1));
                    doc.Insert(pieces[random.Next(pieces.Length)]);
                    break;

                case 2:
                    doc.SetCaret(random.Next(length + 1));
                    doc.SetCaret(random.Next(length + 1), extendSelection: true);
                    doc.DeleteSelection();
                    break;

                case 3:
                    doc.Undo();
                    break;

                case 4:
                    doc.Redo();
                    break;
            }

            // the positions are asked for after each edit: the index is made again each time
            int index = random.Next(doc.Text.Length + 1);
            Assert.Equal(Scanned(doc.Text, index), doc.ToPosition(index));

            var (line, column) = (random.Next(-1, 12), random.Next(-1, 12));
            Assert.Equal(Split(doc.Text, line, column), doc.FromPosition(line, column));
        }

        AssertLikeTheText(doc);
    }
}