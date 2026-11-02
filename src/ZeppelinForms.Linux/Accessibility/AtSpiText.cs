namespace ZeppelinForms.Linux.Accessibility;

/// <summary>The pieces of a text the Text interface hands out by offset: a character,
/// a word, a sentence, a line, a paragraph — what a screen reader reads as the caret
/// moves by them.</summary>
/// <remarks>
/// Offsets count UTF-16 code units, as the core's caret does; AT-SPI counts characters,
/// and the two differ only beyond the BMP — an emoji in a field is off by one after it,
/// which reads a character late, not wrong.
/// </remarks>
internal static class AtSpiText
{
    /// <summary>The text between two offsets; an end of −1 is the end of the text.</summary>
    public static string Slice(string text, int start, int end)
    {
        if (end < 0 || end > text.Length) end = text.Length;

        start = Math.Clamp(start, 0, end);

        return text[start..end];
    }

    /// <summary>The piece at an offset by a granularity of GetStringAtOffset:
    /// (text, start, end); empty past the end.</summary>
    public static (string Text, int Start, int End) At(string text, int offset, uint granularity)
    {
        if (offset < 0 || offset > text.Length || text.Length == 0) return (string.Empty, 0, 0);

        (int start, int end) = granularity switch
        {
            AtSpi.GranularityChar => offset < text.Length ? (offset, offset + 1) : (offset, offset),
            AtSpi.GranularityWord => Word(text, offset),
            AtSpi.GranularitySentence => Sentence(text, offset),
            AtSpi.GranularityLine or AtSpi.GranularityParagraph => Line(text, offset),
            _ => (offset, offset),
        };

        return (text[start..end], start, end);
    }

    /// <summary>The piece at an offset by a boundary type of the older GetTextAtOffset
    /// and its Before and After siblings: the start-boundaries take the piece with what
    /// follows it, the end-boundaries with what precedes it, which here is the same piece
    /// — a screen reader only needs which word or line the caret is in.</summary>
    public static (string Text, int Start, int End) AtBoundary(string text, int offset, uint boundary) =>
        At(text, offset, boundary switch
        {
            AtSpi.BoundaryChar => AtSpi.GranularityChar,
            AtSpi.BoundaryWordStart or AtSpi.BoundaryWordEnd => AtSpi.GranularityWord,
            AtSpi.BoundarySentenceStart or AtSpi.BoundarySentenceEnd => AtSpi.GranularitySentence,
            _ => AtSpi.GranularityLine,
        });

    /// <summary>The piece before the one at an offset; empty at the start.</summary>
    public static (string Text, int Start, int End) Before(string text, int offset, uint boundary)
    {
        (_, int start, _) = AtBoundary(text, offset, boundary);

        return start > 0 ? AtBoundary(text, start - 1, boundary) : (string.Empty, 0, 0);
    }

    /// <summary>The piece after the one at an offset; empty at the end.</summary>
    public static (string Text, int Start, int End) After(string text, int offset, uint boundary)
    {
        (_, _, int end) = AtBoundary(text, offset, boundary);

        return end < text.Length ? AtBoundary(text, end, boundary) : (string.Empty, text.Length, text.Length);
    }

    /// <summary>The word an offset is in, with the spaces after it, as a word-start
    /// boundary takes it; between words, the word that follows.</summary>
    private static (int Start, int End) Word(string text, int offset)
    {
        int start = Math.Min(offset, text.Length);

        while (start > 0 && !char.IsWhiteSpace(text[start - 1])) start--;

        int end = start;

        while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
        while (end < text.Length && char.IsWhiteSpace(text[end]) && text[end] != '\n') end++;

        return (start, end);
    }

    /// <summary>The sentence an offset is in: from after the last '.', '!', '?' or line
    /// break before it to after the next one, its spaces included.</summary>
    private static (int Start, int End) Sentence(string text, int offset)
    {
        static bool Ends(char c) => c is '.' or '!' or '?' or '\n';

        int start = Math.Min(offset, text.Length);

        while (start > 0 && !Ends(text[start - 1])) start--;
        while (start < text.Length && char.IsWhiteSpace(text[start]) && start < offset) start++;

        int end = Math.Min(offset, text.Length);

        while (end < text.Length && !Ends(text[end])) end++;
        if (end < text.Length) end++;
        while (end < text.Length && text[end] == ' ') end++;

        return (start, end);
    }

    /// <summary>The line an offset is in, its line break included.</summary>
    private static (int Start, int End) Line(string text, int offset)
    {
        // the break before the offset, not at it: the offset right after a break is
        // the start of the next line
        offset = Math.Min(offset, text.Length);
        int before = offset > 0 ? text.LastIndexOf('\n', offset - 1) : -1;
        int start = before + 1;

        int end = text.IndexOf('\n', start);
        end = end < 0 ? text.Length : end + 1;

        return (start, end);
    }

    /// <summary>What changed between two texts, as the TextChanged events tell it: the
    /// part deleted from the old one and the part inserted in its place, at one offset.
    /// Null — nothing changed.</summary>
    public static (int Offset, string Deleted, string Inserted)? Difference(string before, string after)
    {
        if (before == after) return null;

        int prefix = 0;
        int shorter = Math.Min(before.Length, after.Length);

        while (prefix < shorter && before[prefix] == after[prefix]) prefix++;

        int suffix = 0;

        while (suffix < shorter - prefix &&
               before[before.Length - 1 - suffix] == after[after.Length - 1 - suffix])
        {
            suffix++;
        }

        return (prefix,
            before.Substring(prefix, before.Length - prefix - suffix),
            after.Substring(prefix, after.Length - prefix - suffix));
    }
}
