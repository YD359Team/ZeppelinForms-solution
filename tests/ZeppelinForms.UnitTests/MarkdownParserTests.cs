using Xunit;
using ZeppelinForms.Core.Markdown;

namespace ZeppelinForms.UnitTests;

public class MarkdownParserTests
{
    private static IReadOnlyList<MarkdownBlock> Blocks(string markdown) => MarkdownDocument.Parse(markdown).Blocks;

    private static IReadOnlyList<MarkdownInline> Inlines(string markdown) =>
        Assert.IsType<ParagraphBlock>(Assert.Single(Blocks(markdown))).Inlines;

    // ===== blocks =====

    [Fact]
    public void HeadingsOfBothKinds()
    {
        IReadOnlyList<MarkdownBlock> blocks = Blocks("# One\n\n### Three ###\n\nTwo\n---\n\nUno\n===");

        Assert.Equal([1, 3, 2, 1], blocks.Cast<HeadingBlock>().Select(h => h.Level));
        Assert.Equal("Three", MarkdownInline.PlainText(((HeadingBlock)blocks[1]).Inlines));
    }

    [Fact]
    public void HeadingAnchorsAreSlugsAndRepeatsAreNumbered()
    {
        IReadOnlyList<MarkdownBlock> blocks = Blocks("# Getting Started!\n# Getting Started!\n## Привет, мир");

        Assert.Equal(["getting-started", "getting-started-1", "привет-мир"], blocks.Cast<HeadingBlock>().Select(h => h.Anchor));
    }

    [Fact]
    public void AHashWithoutASpaceIsText()
    {
        Assert.IsType<ParagraphBlock>(Assert.Single(Blocks("#hashtag")));
    }

    [Fact]
    public void ParagraphsAreSeparatedByBlankLinesAndLinesJoin()
    {
        IReadOnlyList<MarkdownBlock> blocks = Blocks("one\ntwo\n\nthree");

        Assert.Equal(2, blocks.Count);
        Assert.Equal("one two", MarkdownInline.PlainText(((ParagraphBlock)blocks[0]).Inlines));
    }

    [Fact]
    public void FencedCodeKeepsItsTextAndLanguage()
    {
        var code = Assert.IsType<CodeBlock>(Assert.Single(Blocks("```csharp\nvar x = 1;\n\n*not emphasis*\n```")));

        Assert.Equal("csharp", code.Language);
        Assert.Equal("var x = 1;\n\n*not emphasis*", code.Code);
    }

    [Fact]
    public void AnUnclosedFenceRunsToTheEnd()
    {
        var code = Assert.IsType<CodeBlock>(Assert.Single(Blocks("~~~\na\nb")));

        Assert.Equal("a\nb", code.Code);
    }

    [Fact]
    public void IndentedCode()
    {
        var code = Assert.IsType<CodeBlock>(Assert.Single(Blocks("    line 1\n      line 2")));

        Assert.Null(code.Language);
        Assert.Equal("line 1\n  line 2", code.Code);
    }

    [Fact]
    public void RulesOfEveryMark()
    {
        Assert.All(Blocks("***\n\n- - -\n\n___"), block => Assert.IsType<RuleBlock>(block));
    }

    [Fact]
    public void QuotesNestAndContinueLazily()
    {
        var quote = Assert.IsType<QuoteBlock>(Assert.Single(Blocks("> # Title\n> text\ngoes on\n>> inner")));

        Assert.IsType<HeadingBlock>(quote.Blocks[0]);
        Assert.Equal("text goes on", MarkdownInline.PlainText(((ParagraphBlock)quote.Blocks[1]).Inlines));
        Assert.IsType<QuoteBlock>(quote.Blocks[2]);
    }

    [Fact]
    public void ATightBulletList()
    {
        var list = Assert.IsType<ListBlock>(Assert.Single(Blocks("- one\n- two\n- three")));

        Assert.False(list.IsOrdered);
        Assert.False(list.IsLoose);
        Assert.Equal(3, list.Items.Count);
    }

    [Fact]
    public void ALooseNumberedListStartingAtFive()
    {
        var list = Assert.IsType<ListBlock>(Assert.Single(Blocks("5. one\n\n6. two")));

        Assert.True(list.IsOrdered);
        Assert.Equal(5, list.Start);
        Assert.True(list.IsLoose);
    }

    [Fact]
    public void NestedListsAndContinuedItems()
    {
        var list = Assert.IsType<ListBlock>(Assert.Single(Blocks("- one\n  more of one\n  - inner\n- two")));

        Assert.Equal(2, list.Items.Count);

        ListItemBlock first = list.Items[0];
        Assert.Equal("one more of one", MarkdownInline.PlainText(((ParagraphBlock)first.Blocks[0]).Inlines));
        Assert.IsType<ListBlock>(first.Blocks[1]);
    }

    [Fact]
    public void ADifferentBulletStartsAnotherList()
    {
        Assert.Equal(2, Blocks("- a\n+ b").Count);
    }

    [Fact]
    public void TaskItems()
    {
        var list = Assert.IsType<ListBlock>(Assert.Single(Blocks("- [ ] todo\n- [x] done\n- plain")));

        Assert.Equal([false, true, null], list.Items.Select(i => i.IsChecked));
        Assert.Equal("todo", MarkdownInline.PlainText(((ParagraphBlock)list.Items[0].Blocks[0]).Inlines));
    }

    [Fact]
    public void ANumberThatDoesNotStartAtOneDoesNotBreakAParagraph()
    {
        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(Blocks("The year was\n1999. Then")));

        Assert.Equal("The year was 1999. Then", MarkdownInline.PlainText(paragraph.Inlines));
    }

    [Fact]
    public void Tables()
    {
        var table = Assert.IsType<TableBlock>(Assert.Single(Blocks(
            "| Name | Count | Note |\n|:-----|------:|:----:|\n| `a\\|b` | 1 |\n| c | 2 | x | extra |")));

        Assert.Equal([TableAlignment.Left, TableAlignment.Right, TableAlignment.Center], table.Alignments);
        Assert.Equal(2, table.Rows.Count);

        // an escaped pipe inside a cell; a short row padded, a long one cut
        Assert.Equal("a|b", MarkdownInline.PlainText(table.Rows[0][0]));
        Assert.Empty(table.Rows[0][2]);
        Assert.Equal(3, table.Rows[1].Count);
    }

    [Fact]
    public void ADelimiterRowThatDoesNotMatchIsNoTable()
    {
        Assert.IsType<ParagraphBlock>(Assert.Single(Blocks("a | b\n--- | --- | ---")));
    }

    // ===== inlines =====

    [Fact]
    public void EmphasisStrongAndStrikethrough()
    {
        IReadOnlyList<MarkdownInline> inlines = Inlines("*em* **strong** ~~gone~~ ***both***");

        Assert.IsType<EmphasisInline>(inlines[0]);
        Assert.IsType<StrongInline>(inlines[2]);
        Assert.IsType<StrikethroughInline>(inlines[4]);

        // three stars: emphasis around strong, or strong around emphasis — both read the same
        var outer = Assert.IsAssignableFrom<ContainerInline>(inlines[6]);
        Assert.IsAssignableFrom<ContainerInline>(Assert.Single(outer.Children));
    }

    [Fact]
    public void UnderscoresInsideWordsStayText()
    {
        IReadOnlyList<MarkdownInline> inlines = Inlines("snake_case_name and _this_");

        Assert.Equal("snake_case_name and ", Assert.IsType<TextInline>(inlines[0]).Text);
        Assert.IsType<EmphasisInline>(inlines[1]);
    }

    [Fact]
    public void AnUnpairedStarIsText()
    {
        Assert.Equal("2 * 3 = 6", MarkdownInline.PlainText(Inlines("2 * 3 = 6")));
        Assert.All(Inlines("2 * 3 = 6"), inline => Assert.IsType<TextInline>(inline));
    }

    [Fact]
    public void CodeSpansKeepWhatIsInside()
    {
        IReadOnlyList<MarkdownInline> inlines = Inlines("use `*x*` and `` `tick` ``");

        Assert.Equal("*x*", Assert.IsType<CodeInline>(inlines[1]).Code);
        Assert.Equal("`tick`", Assert.IsType<CodeInline>(inlines[3]).Code);
    }

    [Fact]
    public void LinksWithTitlesAndNestedEmphasis()
    {
        var link = Assert.IsType<LinkInline>(Assert.Single(Inlines("[**bold** text](https://example.com/a_(b) \"Title\")")));

        Assert.Equal("https://example.com/a_(b)", link.Url);
        Assert.Equal("Title", link.Title);
        Assert.IsType<StrongInline>(link.Children[0]);
    }

    [Fact]
    public void ReferenceLinksResolveFromDefinitionsAnywhere()
    {
        IReadOnlyList<MarkdownBlock> blocks = Blocks("See [the docs][Docs] and [Docs].\n\n[docs]: https://docs.example.com \"Docs\"");

        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(blocks));
        LinkInline[] links = [.. paragraph.Inlines.OfType<LinkInline>()];

        Assert.Equal(2, links.Length);
        Assert.All(links, link => Assert.Equal("https://docs.example.com", link.Url));
    }

    [Fact]
    public void BracketsWithoutADestinationAreText()
    {
        Assert.Equal("[not a link] and [x]", MarkdownInline.PlainText(Inlines("[not a link] and [x]")));
        Assert.Empty(Inlines("[not a link]").OfType<LinkInline>());
    }

    [Fact]
    public void ImagesAndABadgeInsideALink()
    {
        var link = Assert.IsType<LinkInline>(Assert.Single(Inlines("[![build](badge.svg)](https://ci)")));

        var image = Assert.IsType<ImageInline>(Assert.Single(link.Children));
        Assert.Equal("build", image.Alt);
        Assert.Equal("badge.svg", image.Url);
    }

    [Fact]
    public void Autolinks()
    {
        IReadOnlyList<MarkdownInline> inlines = Inlines("<https://a.b> <me@mail.org> see https://example.com/x. and www.zf.dev");

        LinkInline[] links = [.. inlines.OfType<LinkInline>()];

        Assert.Equal(["https://a.b", "mailto:me@mail.org", "https://example.com/x", "http://www.zf.dev"], links.Select(l => l.Url));
    }

    [Fact]
    public void EscapesEntitiesAndBreaks()
    {
        IReadOnlyList<MarkdownInline> inlines = Inlines("\\*not\\* &amp; &#169;  \nnext\\\nlast");

        Assert.Equal("*not* & ©", Assert.IsType<TextInline>(inlines[0]).Text);
        Assert.IsType<LineBreakInline>(inlines[1]);
        Assert.Equal("next", Assert.IsType<TextInline>(inlines[2]).Text);
        Assert.IsType<LineBreakInline>(inlines[3]);
    }

    [Fact]
    public void RawHtmlIsText()
    {
        Assert.Equal("<b>bold</b>", MarkdownInline.PlainText(Inlines("<b>bold</b>")));
    }
}