using System.Text;

namespace ZeppelinForms.Core.Markdown;

// ===== blocks =====

/// <summary>A block of a Markdown document: a heading, a paragraph, a list.</summary>
public abstract record MarkdownBlock;

/// <summary><c># Title</c> to <c>###### Title</c>, or a line underlined with
/// <c>===</c> or <c>---</c>.</summary>
/// <param name="Anchor">The slug a <c>#fragment</c> link finds it by:
/// "Getting started" is <c>getting-started</c>, as on GitHub.</param>
public sealed record HeadingBlock(int Level, IReadOnlyList<MarkdownInline> Inlines, string Anchor) : MarkdownBlock;

public sealed record ParagraphBlock(IReadOnlyList<MarkdownInline> Inlines) : MarkdownBlock;

/// <summary>A fenced or an indented code block, kept as written.</summary>
/// <param name="Language">The info string of the fence: <c>```csharp</c> gives "csharp".</param>
public sealed record CodeBlock(string? Language, string Code) : MarkdownBlock;

/// <summary><c>&gt; quoted</c> text: blocks of their own, quoted.</summary>
public sealed record QuoteBlock(IReadOnlyList<MarkdownBlock> Blocks) : MarkdownBlock;

/// <summary>A bullet or a numbered list.</summary>
/// <param name="Start">The number of the first item of a numbered list.</param>
/// <param name="IsLoose">Items separated by blank lines: spaced as paragraphs,
/// not packed together.</param>
public sealed record ListBlock(bool IsOrdered, int Start, bool IsLoose, IReadOnlyList<ListItemBlock> Items) : MarkdownBlock;

/// <param name="IsChecked">A task item, <c>- [ ]</c> or <c>- [x]</c>: whether it is
/// done. Null — an ordinary item.</param>
public sealed record ListItemBlock(IReadOnlyList<MarkdownBlock> Blocks, bool? IsChecked) : MarkdownBlock;

/// <summary><c>---</c>, <c>***</c> or <c>___</c> on a line of its own.</summary>
public sealed record RuleBlock : MarkdownBlock;

public enum TableAlignment : byte { None, Left, Center, Right }

/// <summary>A GitHub table: a header row, the alignment row under it, body rows.</summary>
public sealed record TableBlock(
    IReadOnlyList<TableAlignment> Alignments,
    IReadOnlyList<IReadOnlyList<MarkdownInline>> Header,
    IReadOnlyList<IReadOnlyList<IReadOnlyList<MarkdownInline>>> Rows) : MarkdownBlock;

// ===== inlines =====

/// <summary>A piece of a paragraph: text, emphasis, a link.</summary>
public abstract record MarkdownInline
{
    /// <summary>The text the inlines read as, formatting dropped: the name a screen
    /// reader gets, the alt text of an image.</summary>
    public static string PlainText(IEnumerable<MarkdownInline> inlines)
    {
        var builder = new StringBuilder();
        Append(builder, inlines);
        return builder.ToString();
    }

    private static void Append(StringBuilder builder, IEnumerable<MarkdownInline> inlines)
    {
        foreach (MarkdownInline inline in inlines)
        {
            switch (inline)
            {
                case TextInline text: builder.Append(text.Text); break;
                case CodeInline code: builder.Append(code.Code); break;
                case LineBreakInline: builder.Append(' '); break;
                case ImageInline image: builder.Append(image.Alt); break;
                case ContainerInline container: Append(builder, container.Children); break;
            }
        }
    }
}

public sealed record TextInline(string Text) : MarkdownInline;

/// <summary><c>`code`</c> inside a paragraph.</summary>
public sealed record CodeInline(string Code) : MarkdownInline;

/// <summary>A line break: two spaces or a backslash at the end of a line.</summary>
public sealed record LineBreakInline : MarkdownInline;

/// <summary><c>![alt](url "title")</c>.</summary>
public sealed record ImageInline(string Alt, string Url, string? Title) : MarkdownInline;

/// <summary>Inlines that wrap other inlines.</summary>
public abstract record ContainerInline(IReadOnlyList<MarkdownInline> Children) : MarkdownInline;

/// <summary><c>*emphasis*</c> or <c>_emphasis_</c>: italic.</summary>
public sealed record EmphasisInline(IReadOnlyList<MarkdownInline> Children) : ContainerInline(Children);

/// <summary><c>**strong**</c> or <c>__strong__</c>: bold.</summary>
public sealed record StrongInline(IReadOnlyList<MarkdownInline> Children) : ContainerInline(Children);

/// <summary><c>~~struck~~</c>.</summary>
public sealed record StrikethroughInline(IReadOnlyList<MarkdownInline> Children) : ContainerInline(Children);

/// <summary><c>[text](url "title")</c>, <c>[text][ref]</c>, <c>&lt;https://…&gt;</c>
/// or a bare <c>https://…</c>.</summary>
public sealed record LinkInline(IReadOnlyList<MarkdownInline> Children, string Url, string? Title) : ContainerInline(Children);