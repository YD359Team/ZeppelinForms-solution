using Xunit;
using ZeppelinForms.Core.Text;

namespace ZeppelinForms.UnitTests;

/// <summary>The Unicode Bidirectional Algorithm: levels and the visual order of runs.
/// Hebrew letters are written as א… so the source reads left to right.</summary>
public class BidiTests
{
    private const string Alef = "א", Bet = "ב", Gimel = "ג";
    private const string Hebrew = Alef + Bet + Gimel;
    private const string Arabic = "عدد";

    /// <summary>The runs of the whole text in visual order, as "text:level" — each run's
    /// text in its logical order.</summary>
    private static string Visual(string text, FlowDirection? direction = null)
    {
        BidiParagraph paragraph = BidiParagraph.Analyze(text, direction);

        return string.Join(" | ", paragraph.LineRuns(0, text.Length)
            .Select(r => $"{Show(text.Substring(r.Start, r.Length))}:{r.Level}"));
    }

    /// <summary>Hebrew letters as A, B, G so failures read.</summary>
    private static string Show(string text) => text
        .Replace(Alef, "A", StringComparison.Ordinal)
        .Replace(Bet, "B", StringComparison.Ordinal)
        .Replace(Gimel, "G", StringComparison.Ordinal);

    [Fact]
    public void TheTableAgreesWithTheAsciiShortcut()
    {
        // the ASCII classes are written out by hand for speed: they must be the database's
        for (char c = '\0'; c < '\u0080'; c++)
            Assert.Equal(BidiData.Get(c), BidiParagraph.AsciiClass(c));

        Assert.Equal(BidiClass.R, BidiData.Get(0x05D0));
        Assert.Equal(BidiClass.AL, BidiData.Get(0x0639));
        Assert.Equal(BidiClass.AN, BidiData.Get(0x0661));
        Assert.Equal(BidiClass.NSM, BidiData.Get(0x0301));
        Assert.Equal(BidiClass.RLI, BidiData.Get(0x2067));
        Assert.Equal(BidiClass.ON, BidiData.Get(0x1F600));
        Assert.Equal(BidiClass.R, BidiData.Get(0x10800));
        Assert.Equal(BidiClass.L, BidiData.Get(0x4E00));
        Assert.Equal(BidiClass.L, BidiData.Get(0x10FFFD));
    }

    [Fact]
    public void LatinAloneIsOneRunWithoutWork()
    {
        BidiParagraph paragraph = BidiParagraph.Analyze("Hello, world 123", null);

        Assert.True(paragraph.IsSimple);
        Assert.False(paragraph.IsRightToLeft);
        Assert.Equal("Hello, world 123:0", Visual("Hello, world 123"));
    }

    [Fact]
    public void HebrewAloneRunsRightToLeft()
    {
        BidiParagraph paragraph = BidiParagraph.Analyze(Hebrew, null);

        Assert.True(paragraph.IsRightToLeft);
        Assert.Equal("ABG:1", Visual(Hebrew));
    }

    [Fact]
    public void AHebrewWordInALatinSentence()
    {
        // the spaces between a left-to-right and a right-to-left word take the paragraph's side
        Assert.Equal("abc :0 | ABG:1 |  def:0", Visual($"abc {Hebrew} def"));
    }

    [Fact]
    public void LatinWordsInARightToLeftParagraph()
    {
        // "ABG abc def": the Latin phrase is one level-2 run, its inner space included
        Assert.Equal("abc def:2 | ABG :1", Visual($"{Hebrew} abc def", FlowDirection.RightToLeft));
    }

    [Fact]
    public void ANumberAfterHebrewReadsLeftToRight()
    {
        Assert.Equal("123:2 | ABG :1", Visual($"{Hebrew} 123"));
    }

    [Fact]
    public void ANumberAfterArabicIsAnArabicNumber()
    {
        BidiParagraph paragraph = BidiParagraph.Analyze($"{Arabic} 12", null);

        // W2 makes the digits AN, and they still read left to right at level 2
        Assert.Equal(2, paragraph.LevelAt(Arabic.Length + 1));
        Assert.Equal(2, paragraph.LevelAt(Arabic.Length + 2));
    }

    [Fact]
    public void ANumberWithItsSignsStaysTogether()
    {
        // "-12.5%" after Hebrew: separators and terminators join the number
        string text = $"{Hebrew} 12.5%";
        BidiParagraph paragraph = BidiParagraph.Analyze(text, null);

        for (int i = Hebrew.Length + 1; i < text.Length; i++)
            Assert.Equal(2, paragraph.LevelAt(i));
    }

    [Fact]
    public void BracketsFollowWhatTheyEnclose()
    {
        // a right-to-left paragraph with a Latin word in brackets: the brackets
        // enclose L, the context before is R — they take the embedding direction
        string text = $"{Hebrew} (abc)";
        BidiParagraph paragraph = BidiParagraph.Analyze(text, null);

        Assert.Equal(1, paragraph.LevelAt(Hebrew.Length + 1));
        Assert.Equal(1, paragraph.LevelAt(text.Length - 1));
        Assert.Equal(2, paragraph.LevelAt(Hebrew.Length + 2));
    }

    [Fact]
    public void BracketsInLeftToRightTextAroundHebrew()
    {
        // "a (ABG) b": the brackets enclose R only; before them is L, so they stay L
        string text = $"a ({Hebrew}) b";
        BidiParagraph paragraph = BidiParagraph.Analyze(text, FlowDirection.LeftToRight);

        Assert.Equal(0, paragraph.LevelAt(2));
        Assert.Equal(0, paragraph.LevelAt(3 + Hebrew.Length));
        Assert.Equal(1, paragraph.LevelAt(3));
    }

    [Fact]
    public void TrailingWhitespaceGoesBackToTheParagraph()
    {
        // L1: inside a right-to-left embedding the trailing spaces resolve to level 1,
        // and at the line's end they go back to the paragraph's 0
        string text = "\u202Babc  ";
        BidiParagraph paragraph = BidiParagraph.Analyze(text, FlowDirection.LeftToRight);

        Assert.Equal(1, paragraph.LevelAt(text.Length - 1));

        BidiRun[] runs = paragraph.LineRuns(0, text.Length);

        Assert.Equal(0, runs[^1].Level);
        Assert.Equal(text.Length - 2, runs[^1].Start);
    }

    [Fact]
    public void AnOverrideReversesLatin()
    {
        // RLO … PDF: the letters are forced right to left
        string text = "a‮bcd‬e";
        BidiParagraph paragraph = BidiParagraph.Analyze(text, null);

        Assert.Equal(1, paragraph.LevelAt(2));
        Assert.Equal(1, paragraph.LevelAt(3));
        Assert.Equal(0, paragraph.LevelAt(text.Length - 1));
    }

    [Fact]
    public void AnIsolateKeepsItsInsideApart()
    {
        // FSI … PDI around Hebrew inside a Latin sentence: the isolate is R inside,
        // and the neutrals after it don't see its content
        string text = $"a ⁨{Hebrew}⁩ 1";
        BidiParagraph paragraph = BidiParagraph.Analyze(text, null);

        Assert.False(paragraph.IsRightToLeft);
        Assert.Equal(1, paragraph.LevelAt(3));
        Assert.Equal(0, paragraph.LevelAt(text.Length - 1));
    }

    [Fact]
    public void TheParagraphTakesItsFirstStrongCharacterSkippingIsolates()
    {
        // the first strong character is inside an isolate — skipped; then Hebrew
        string text = $"⁦abc⁩{Hebrew}";

        Assert.True(BidiParagraph.Analyze(text, null).IsRightToLeft);
    }

    [Fact]
    public void ASurrogatePairStaysOneCharacter()
    {
        string text = $"{Hebrew}\U0001F600{Hebrew}";
        BidiParagraph paragraph = BidiParagraph.Analyze(text, null);

        Assert.Equal(paragraph.LevelAt(3), paragraph.LevelAt(4));

        BidiRun[] runs = paragraph.LineRuns(0, text.Length);
        Assert.Single(runs);
    }

    [Fact]
    public void ANonspacingMarkFollowsItsLetter()
    {
        // a combining acute after Latin inside Hebrew text takes the letter's level
        string text = $"{Hebrew} é";
        BidiParagraph paragraph = BidiParagraph.Analyze(text, null);

        Assert.Equal(paragraph.LevelAt(text.Length - 2), paragraph.LevelAt(text.Length - 1));
    }

    [Fact]
    public void EachParagraphHasItsOwnDirection()
    {
        string text = $"abc\n{Hebrew}";
        BidiParagraph paragraph = BidiParagraph.Analyze(text, null);

        Assert.Equal(0, paragraph.LevelAt(0));
        Assert.Equal(1, paragraph.LevelAt(4));
        Assert.False(paragraph.IsRightToLeft);
    }

    [Fact]
    public void ALineOfAWrappedParagraphIsReorderedAlone()
    {
        string text = $"{Hebrew} abc {Hebrew}";
        BidiParagraph paragraph = BidiParagraph.Analyze(text, null);

        // the second row: "abc ABG" — its trailing letters are Hebrew, no whitespace rule
        BidiRun[] second = paragraph.LineRuns(4, text.Length - 4);

        Assert.Equal(2, second.Length);
        Assert.Equal(4 + 3, second[0].Start);
        Assert.Equal(1, second[0].Level);
        Assert.Equal(4, second[1].Start);
        Assert.Equal(2, second[1].Level);
    }

    [Fact]
    public void AnEmptyTextHasNoRuns()
    {
        Assert.Empty(BidiParagraph.Analyze(string.Empty, null).LineRuns(0, 0));
        Assert.Empty(BidiParagraph.Analyze(string.Empty, FlowDirection.RightToLeft).LineRuns(0, 0));
    }
}
