namespace ZeppelinForms.LanguageServer.Markup;

/// <summary>Where in the markup a position is.</summary>
internal enum MarkupPlace
{
    /// <summary>Between tags: a new element can start here.</summary>
    Content,

    /// <summary>In the name of a start tag: <c>&lt;Butt|</c>.</summary>
    ElementName,

    /// <summary>In a start tag, where an attribute's name goes.</summary>
    AttributeName,

    /// <summary>In an attribute's value, between its quotes.</summary>
    AttributeValue,

    /// <summary>A comment, a declaration, an end tag: nothing to offer.</summary>
    None,
}

/// <summary>An element of the markup as far as it is written: its name and its attributes.</summary>
internal sealed class MarkupElement(string name, int start)
{
    /// <summary>As written: <c>Button</c>, <c>vm:Card</c>, <c>Grid.Styles</c>.</summary>
    public string Name { get; } = name;

    public int Start { get; } = start;

    public Dictionary<string, string> Attributes { get; } = new(StringComparer.Ordinal);

    /// <summary><c>Owner.Property</c>: a property element, not an element to create.</summary>
    public bool IsPropertyElement => Name.Contains('.');
}

/// <summary>What surrounds a position of the markup: the elements it is inside, the tag
/// being written, and the word typed so far.</summary>
internal sealed class MarkupContext
{
    public MarkupPlace Place { get; init; }

    /// <summary>The elements around the position, the outermost first; the tag being written
    /// is not among them.</summary>
    public required IReadOnlyList<MarkupElement> Ancestors { get; init; }

    /// <summary>The start tag the position is in, with all its attributes — those after the
    /// position too. Null in content.</summary>
    public MarkupElement? Tag { get; init; }

    /// <summary>The attribute whose name or value the position is in.</summary>
    public string? Attribute { get; init; }

    /// <summary>The value up to the position, for <see cref="MarkupPlace.AttributeValue"/>.</summary>
    public string ValueBefore { get; init; } = string.Empty;

    /// <summary>Where the value starts, after its quote.</summary>
    public int ValueStart { get; init; }

    /// <summary>The name being typed: of an element, of an attribute.</summary>
    public string Word { get; init; } = string.Empty;

    /// <summary>Where <see cref="Word"/> starts: completion replaces from there.</summary>
    public int WordStart { get; init; }

    /// <summary>The innermost element whose attribute it is: the tag, else the nearest ancestor.</summary>
    public IEnumerable<MarkupElement> InnermostFirst =>
        (Tag is null ? Ancestors : Ancestors.Append(Tag)).Reverse();

    /// <summary>The namespace of a prefix: the xmlns of the nearest element that declares it.</summary>
    public string? NamespaceOf(string prefix)
    {
        foreach (MarkupElement element in InnermostFirst)
            if (element.Attributes.TryGetValue("xmlns:" + prefix, out string? value))
                return value;

        return null;
    }

    /// <summary>The <c>x:DataType</c> in force: of the tag, or of the nearest ancestor that says it.</summary>
    public string? DataType =>
        InnermostFirst.Select(e => e.Attributes.TryGetValue("x:DataType", out string? type) ? type : null).FirstOrDefault(t => t is not null);
}

/// <summary>
/// Reads markup up to a position the way an editor needs it: forgiving — the text is being
/// typed, a tag is open, a quote is missing — and only as far as the position, plus the rest
/// of the tag the position is in.
/// </summary>
internal static class MarkupScanner
{
    public static MarkupContext At(string text, int position)
    {
        var stack = new List<MarkupElement>();
        int i = 0;

        while (i < position)
        {
            if (text[i] != '<')
            {
                i++;
                continue;
            }

            // a comment, a declaration, CDATA: skipped whole; the position inside one is nothing
            if (Skip(text, ref i, "<!--", "-->", position) || Skip(text, ref i, "<?", "?>", position) ||
                Skip(text, ref i, "<![CDATA[", "]]>", position) || Skip(text, ref i, "<!", ">", position))
            {
                if (i > position) return Nothing(stack);
                continue;
            }

            // an end tag closes up to its element
            if (i + 1 < text.Length && text[i + 1] == '/')
            {
                int close = text.IndexOf('>', i);
                if (close < 0 || close >= position) return Nothing(stack);

                string name = text[(i + 2)..close].Trim();
                int index = stack.FindLastIndex(e => e.Name == name);
                if (index >= 0) stack.RemoveRange(index, stack.Count - index);

                i = close + 1;
                continue;
            }

            // a start tag
            int start = i;
            int nameStart = i + 1;
            int nameEnd = nameStart;

            while (nameEnd < text.Length && IsNameChar(text[nameEnd])) nameEnd++;

            if (position <= nameEnd)
            {
                return new MarkupContext
                {
                    Place = MarkupPlace.ElementName,
                    Ancestors = [.. stack],
                    Word = text[nameStart..position],
                    WordStart = nameStart,
                };
            }

            var element = new MarkupElement(text[nameStart..nameEnd], start);
            TagEnd end = ReadAttributes(text, nameEnd, element, position, out MarkupContext? inside, stack);

            if (inside is not null) return inside;

            if (position < end.Position || (!end.Closed && end.Position >= position))
            {
                // still in the tag, between attributes
                return new MarkupContext
                {
                    Place = MarkupPlace.AttributeName,
                    Ancestors = [.. stack],
                    Tag = element,
                    WordStart = position,
                };
            }

            if (!end.SelfClosing) stack.Add(element);

            i = end.Position;
        }

        return new MarkupContext { Place = MarkupPlace.Content, Ancestors = [.. stack], WordStart = position };
    }

    private readonly record struct TagEnd(int Position, bool Closed, bool SelfClosing);

    /// <summary>The attributes of a start tag from after its name; where the tag ends. A
    /// position inside an attribute gives its context, and the rest of the tag is still read
    /// for the attributes after it.</summary>
    private static TagEnd ReadAttributes(
        string text, int from, MarkupElement element, int position, out MarkupContext? inside, List<MarkupElement> stack)
    {
        inside = null;
        int i = from;

        while (i < text.Length)
        {
            char c = text[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '>') return new TagEnd(i + 1, true, false);

            if (c == '/' && i + 1 < text.Length && text[i + 1] == '>') return new TagEnd(i + 2, true, true);

            // a new tag before this one closed: the tag was left unfinished
            if (c == '<') return new TagEnd(i, false, false);

            if (!IsNameChar(c))
            {
                i++;
                continue;
            }

            int nameStart = i;
            while (i < text.Length && IsNameChar(text[i])) i++;

            string name = text[nameStart..i];

            if (inside is null && position >= nameStart && position <= i)
            {
                inside = new MarkupContext
                {
                    Place = MarkupPlace.AttributeName,
                    Ancestors = [.. stack],
                    Tag = element,
                    Attribute = name,
                    Word = text[nameStart..position],
                    WordStart = nameStart,
                };
            }

            int j = i;
            while (j < text.Length && char.IsWhiteSpace(text[j])) j++;

            if (j >= text.Length || text[j] != '=')
            {
                i = j;
                continue;
            }

            j++;
            while (j < text.Length && char.IsWhiteSpace(text[j])) j++;

            if (j >= text.Length || text[j] is not ('"' or '\''))
            {
                i = j;
                continue;
            }

            char quote = text[j];
            int valueStart = j + 1;
            int valueEnd = text.IndexOf(quote, valueStart);

            // an unclosed value runs to the end of its line at most
            if (valueEnd < 0)
            {
                int line = text.IndexOf('\n', valueStart);
                valueEnd = line < 0 ? text.Length : line;
            }

            if (inside is null && position >= valueStart && position <= valueEnd)
            {
                inside = new MarkupContext
                {
                    Place = MarkupPlace.AttributeValue,
                    Ancestors = [.. stack],
                    Tag = element,
                    Attribute = name,
                    ValueBefore = text[valueStart..position],
                    ValueStart = valueStart,
                    WordStart = valueStart,
                };
            }

            element.Attributes[name] = text[valueStart..Math.Min(valueEnd, text.Length)];
            i = Math.Min(valueEnd + 1, text.Length);
        }

        return new TagEnd(text.Length, false, false);
    }

    private static bool Skip(string text, ref int i, string open, string close, int position)
    {
        if (string.CompareOrdinal(text, i, open, 0, open.Length) != 0) return false;

        int end = text.IndexOf(close, i + open.Length, StringComparison.Ordinal);

        // unclosed, or the position inside it: past the position, which says "nothing"
        i = end < 0 || end + close.Length > position ? position + 1 : end + close.Length;
        return true;
    }

    private static MarkupContext Nothing(List<MarkupElement> stack) =>
        new() { Place = MarkupPlace.None, Ancestors = [.. stack] };

    public static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c is '_' or ':' or '.' or '-';

    /// <summary>The named elements of the whole markup: <c>Name="Search"</c> on a
    /// <c>TextBox</c> — name → the element's name as written.</summary>
    public static Dictionary<string, string> NamedElements(string text)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        int i = 0;

        while ((i = text.IndexOf('<', i)) >= 0)
        {
            int nameStart = i + 1;

            if (nameStart >= text.Length || !char.IsLetter(text[nameStart]))
            {
                i++;
                continue;
            }

            int nameEnd = nameStart;
            while (nameEnd < text.Length && IsNameChar(text[nameEnd])) nameEnd++;

            var element = new MarkupElement(text[nameStart..nameEnd], i);
            TagEnd end = ReadAttributes(text, nameEnd, element, -1, out _, []);

            string? name = element.Attributes.TryGetValue("x:Name", out string? x) ? x
                : element.Attributes.TryGetValue("Name", out string? plain) ? plain : null;

            if (name is not null && !element.IsPropertyElement) names.TryAdd(name, element.Name);

            i = Math.Max(end.Position, i + 1);
        }

        return names;
    }
}
