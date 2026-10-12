using Xunit;
using ZeppelinForms.Core.Text;
using ZeppelinForms.Drawing;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>A line laid out as it is drawn: carets, clicks and selections in mixed text.
/// The headless measurer gives every UTF-16 unit 6 pixels at size 10.</summary>
public class TextLineLayoutTests
{
    private const string Hebrew = "אבג";

    private static readonly Font Ten = Font.Default.WithSize(10);

    private static TextLineLayout Lay(string text, FlowDirection direction = FlowDirection.LeftToRight) =>
        ((ITextMeasurer)new HeadlessTextMeasurer()).LayOutLine(text, Ten, direction);

    [Fact]
    public void LatinCaretsAddUp()
    {
        TextLineLayout line = Lay("abc");

        Assert.Equal(18, line.Width, 0.01f);
        Assert.Equal([0f, 6f, 12f, 18f], Enumerable.Range(0, 4).Select(line.CaretX));
    }

    [Fact]
    public void HebrewRunsFromTheRight()
    {
        TextLineLayout line = Lay(Hebrew, FlowDirection.RightToLeft);

        // the first letter stands at the right end; the caret before it is at its right edge
        Assert.Equal((12f, 18f), line.GetCharacterBounds(0));
        Assert.Equal(18, line.CaretX(0), 0.01f);
        Assert.Equal(0, line.CaretX(3), 0.01f);
    }

    [Fact]
    public void AMixedLinePlacesEachWordInItsDirection()
    {
        // "abc ABG": the Hebrew word stands right of the Latin one, its letters reversed
        TextLineLayout line = Lay($"abc {Hebrew}");

        Assert.Equal(2, line.Runs.Count);
        Assert.False(line.Runs[0].IsRightToLeft);
        Assert.True(line.Runs[1].IsRightToLeft);

        Assert.Equal((36f, 42f), line.GetCharacterBounds(4));
        Assert.Equal((24f, 30f), line.GetCharacterBounds(6));

        // the caret before the Hebrew word stands at its right end, after it at its left
        Assert.Equal(42, line.CaretX(4), 0.01f);
        Assert.Equal(24, line.CaretX(7), 0.01f);
    }

    [Fact]
    public void AClickPicksTheNearerEdgeOfTheCharacter()
    {
        TextLineLayout line = Lay($"abc {Hebrew}");

        Assert.Equal(1, line.IndexAt(7));    // right half of 'a'
        Assert.Equal(0, line.IndexAt(2));    // left half of 'a'

        // the right half of a Hebrew letter is its start, the left half its end
        Assert.Equal(4, line.IndexAt(41));
        Assert.Equal(5, line.IndexAt(37));

        // beyond the ends
        Assert.Equal(0, line.IndexAt(-5));
        Assert.Equal(4, line.IndexAt(100));
    }

    [Fact]
    public void ASelectionAcrossDirectionsIsSeveralSpans()
    {
        TextLineLayout line = Lay($"abc {Hebrew}");

        // "c A": the 'c' and the space at the left, the alef at the far right
        IReadOnlyList<(float Left, float Right)> spans = line.SelectionBounds(2, 5);

        Assert.Equal(2, spans.Count);
        Assert.Equal((12f, 24f), spans[0]);
        Assert.Equal((36f, 42f), spans[1]);
    }

    [Fact]
    public void TheCaretMovesAcrossTheScreenNotTheStorage()
    {
        TextLineLayout line = Lay($"abc{Hebrew}");

        // positions on the screen, left to right: 0 1 2 | 6 5 4 3
        var visited = new List<int> { 0 };
        int index = 0;

        while (line.MoveVisually(index, right: true) is { } next)
        {
            visited.Add(next);
            index = next;
        }

        Assert.Equal([0, 1, 2, 6, 5, 4, 3], visited);
        Assert.Null(line.MoveVisually(3, right: true));
        Assert.Equal(4, line.MoveVisually(3, right: false));
    }

    [Fact]
    public void ACaretNeverStopsInsideASurrogatePair()
    {
        TextLineLayout line = Lay("a\U0001F600b");

        Assert.Equal(3, line.MoveVisually(1, right: true));
        Assert.Contains(line.IndexAt(line.CaretX(2)), new[] { 1, 3 });
    }

    [Fact]
    public void AnEmptyLineHasItsCaretAtZero()
    {
        TextLineLayout line = Lay(string.Empty);

        Assert.Equal(0, line.CaretX(0));
        Assert.Equal(0, line.IndexAt(50));
        Assert.Empty(line.SelectionBounds(0, 0));
        Assert.Null(line.MoveVisually(0, right: true));
    }
}
