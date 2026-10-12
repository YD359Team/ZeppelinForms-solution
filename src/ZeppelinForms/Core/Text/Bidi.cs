namespace ZeppelinForms.Core.Text;

/// <summary>The bidirectional class of a character, as the Unicode Character Database
/// gives it (UAX #9, table 4).</summary>
internal enum BidiClass : byte
{
    /// <summary>Left-to-right: Latin, Cyrillic, CJK…</summary>
    L,
    /// <summary>Right-to-left: Hebrew.</summary>
    R,
    /// <summary>Right-to-left Arabic.</summary>
    AL,
    /// <summary>European number.</summary>
    EN,
    /// <summary>European separator: plus, minus.</summary>
    ES,
    /// <summary>European terminator: currency signs, percent.</summary>
    ET,
    /// <summary>Arabic number.</summary>
    AN,
    /// <summary>Common separator: comma, colon, period.</summary>
    CS,
    /// <summary>Nonspacing mark.</summary>
    NSM,
    /// <summary>Boundary neutral: format and control characters.</summary>
    BN,
    /// <summary>Paragraph separator.</summary>
    B,
    /// <summary>Segment separator: tab.</summary>
    S,
    /// <summary>Whitespace.</summary>
    WS,
    /// <summary>Other neutrals.</summary>
    ON,
    LRE,
    LRO,
    RLE,
    RLO,
    PDF,
    LRI,
    RLI,
    FSI,
    PDI,
}

internal static partial class BidiData
{
    /// <summary>The class of a code point.</summary>
    public static BidiClass Get(int codePoint)
    {
        ReadOnlySpan<int> starts = Starts;

        int index = starts.BinarySearch(codePoint);
        if (index < 0) index = ~index - 1;

        return (BidiClass)Classes[index];
    }
}

/// <summary>A piece of a line at one embedding level: odd levels run right to left.</summary>
/// <param name="Start">Its first UTF-16 index in the text.</param>
/// <param name="Length">Its length in UTF-16 units.</param>
/// <param name="Level">Its embedding level.</param>
internal readonly record struct BidiRun(int Start, int Length, byte Level)
{
    public bool IsRightToLeft => (Level & 1) != 0;

    public int End => Start + Length;
}

/// <summary>
/// The Unicode Bidirectional Algorithm (UAX #9): the embedding level of every character
/// of a text, and the visual order of the runs of a line of it.
/// </summary>
/// <remarks>
/// <para>
/// The levels are resolved once per text, the reordering per line: a wrapped paragraph
/// keeps the levels of the whole and reorders each row on its own, as rule L1 wants —
/// the whitespace at a row's end goes back to the paragraph's direction.
/// </para>
/// <para>
/// All of the algorithm is here — explicit embeddings, overrides and isolates (X1–X10),
/// weak and neutral types (W1–W7, N0–N2) with bracket pairs, implicit levels (I1–I2),
/// line reordering (L1–L2). Mirroring of brackets in right-to-left runs (L4) is the
/// shaper's work. A paragraph separator inside the text starts a new paragraph.
/// </para>
/// <para>
/// Text with nothing right-to-left in it — nothing at or above U+0590 — in a
/// left-to-right paragraph is told apart at once and costs no arrays.
/// </para>
/// </remarks>
internal sealed class BidiParagraph
{
    /// <summary>The deepest explicit embedding (BD2).</summary>
    private const int MaxDepth = 125;

    /// <summary>How many brackets may be open at once in BD16.</summary>
    private const int MaxBracketDepth = 63;

    private static readonly BidiParagraph SimpleEmpty = new(string.Empty, 0, null, null, null);

    private readonly string _text;

    /// <summary>Resolved levels, before the line rules. Null — all zero.</summary>
    private readonly byte[]? _levels;

    /// <summary>The classes as the database gives them. Null with the levels.</summary>
    private readonly BidiClass[]? _classes;

    /// <summary>The level of the paragraph each character is in. Null with the levels.</summary>
    private readonly byte[]? _paragraphLevels;

    private BidiParagraph(string text, byte level, byte[]? levels, BidiClass[]? classes, byte[]? paragraphLevels)
    {
        _text = text;
        Level = level;
        _levels = levels;
        _classes = classes;
        _paragraphLevels = paragraphLevels;
    }

    /// <summary>The level of the (first) paragraph: 0 left to right, 1 right to left.</summary>
    public byte Level { get; }

    public bool IsRightToLeft => (Level & 1) != 0;

    /// <summary>Nothing in the text runs right to left: one run at level 0.</summary>
    public bool IsSimple => _levels is null;

    /// <summary>The resolved level of a character, before the line rules.</summary>
    public byte LevelAt(int index) => _levels?[index] ?? 0;

    /// <summary>Resolve the levels of a text.</summary>
    /// <param name="text">The text, one or more paragraphs.</param>
    /// <param name="direction">The paragraphs' direction; null — each paragraph takes
    /// the direction of its first strong character (P2–P3), left to right if it has none.</param>
    public static BidiParagraph Analyze(string text, FlowDirection? direction)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (direction != FlowDirection.RightToLeft && !MayHaveRightToLeft(text))
            return text.Length == 0 ? SimpleEmpty : new BidiParagraph(text, 0, null, null, null);

        int n = text.Length;
        var classes = new BidiClass[n];

        for (int i = 0; i < n; i++)
        {
            char c = text[i];

            if (char.IsHighSurrogate(c) && i + 1 < n && char.IsLowSurrogate(text[i + 1]))
            {
                classes[i] = BidiData.Get(char.ConvertToUtf32(c, text[i + 1]));

                // the second half takes no part: removed as a boundary neutral, and
                // given the level of the first at the end
                classes[++i] = BidiClass.BN;
            }
            else
            {
                classes[i] = c < 0x80 ? AsciiClass(c) : BidiData.Get(c);
            }
        }

        var levels = new byte[n];
        var paragraphLevels = new byte[n];
        byte first = 0;

        int start = 0;

        for (int i = 0; i <= n; i++)
        {
            if (i < n && classes[i] != BidiClass.B) continue;

            int end = Math.Min(n, i + 1);

            if (end > start || n == 0)
            {
                byte level = ResolveParagraph(text, classes, levels, start, end, direction);

                paragraphLevels.AsSpan(start, end - start).Fill(level);

                if (start == 0) first = level;
            }

            start = end;
        }

        // the second half of a surrogate pair goes with the first
        for (int i = 1; i < n; i++)
            if (char.IsLowSurrogate(text[i]) && char.IsHighSurrogate(text[i - 1]))
                levels[i] = levels[i - 1];

        return new BidiParagraph(text, first, levels, classes, paragraphLevels);
    }

    /// <summary>The runs of a line in visual order, left to right on the screen: the
    /// line rules applied (L1) and the runs reversed (L2).</summary>
    /// <param name="start">The line's first index in the text.</param>
    /// <param name="length">The line's length.</param>
    public BidiRun[] LineRuns(int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(start + length, _text.Length);

        if (length == 0) return [];

        if (_levels is null)
            return [new BidiRun(start, length, 0)];

        byte[] line = _levels.AsSpan(start, length).ToArray();
        ApplyLineRules(line, start);

        // the logical runs
        var runs = new List<BidiRun>();
        int runStart = 0;

        for (int i = 1; i <= length; i++)
        {
            if (i < length && line[i] == line[runStart]) continue;

            runs.Add(new BidiRun(start + runStart, i - runStart, line[runStart]));
            runStart = i;
        }

        // L2: from the highest level down to the lowest odd one, reverse every
        // sequence of runs at that level or higher
        byte highest = 0, lowestOdd = byte.MaxValue;

        foreach (BidiRun run in runs)
        {
            highest = Math.Max(highest, run.Level);

            if ((run.Level & 1) != 0) lowestOdd = Math.Min(lowestOdd, run.Level);
        }

        for (int level = highest; level >= lowestOdd && level > 0; level--)
        {
            for (int i = 0; i < runs.Count; i++)
            {
                if (runs[i].Level < level) continue;

                int j = i;
                while (j + 1 < runs.Count && runs[j + 1].Level >= level) j++;

                runs.Reverse(i, j - i + 1);
                i = j;
            }
        }

        return [.. runs];
    }

    /// <summary>L1: separators, and the whitespace before them and at the line's end,
    /// go back to the paragraph's level.</summary>
    private void ApplyLineRules(byte[] line, int start)
    {
        BidiClass[] classes = _classes!;
        byte[] paragraph = _paragraphLevels!;
        int length = line.Length;

        for (int i = 0; i < length; i++)
        {
            BidiClass type = classes[start + i];

            if (type is not (BidiClass.S or BidiClass.B)) continue;

            line[i] = paragraph[start + i];

            for (int j = i - 1; j >= 0 && IsTrailingWhitespace(classes[start + j]); j--)
                line[j] = paragraph[start + j];
        }

        for (int j = length - 1; j >= 0 && IsTrailingWhitespace(classes[start + j]); j--)
            line[j] = paragraph[start + j];

        for (int i = 1; i < length; i++)
            if (char.IsLowSurrogate(_text[start + i]) && char.IsHighSurrogate(_text[start + i - 1]))
                line[i] = line[i - 1];
    }

    /// <summary>Whitespace for L1: what goes back to the paragraph's level at a line's end.
    /// The characters X9 removed count too — they have no level of their own.</summary>
    private static bool IsTrailingWhitespace(BidiClass type) => type is
        BidiClass.WS or BidiClass.FSI or BidiClass.LRI or BidiClass.RLI or BidiClass.PDI or
        BidiClass.BN or BidiClass.LRE or BidiClass.RLE or BidiClass.LRO or BidiClass.RLO or BidiClass.PDF;

    /// <summary>No right-to-left character, no explicit formatting can be below U+0590.</summary>
    private static bool MayHaveRightToLeft(string text)
    {
        foreach (char c in text)
            if (c >= '֐') return true;

        return false;
    }

    internal static BidiClass AsciiClass(char c) => c switch
    {
        >= '0' and <= '9' => BidiClass.EN,
        >= 'a' and <= 'z' or >= 'A' and <= 'Z' => BidiClass.L,
        '+' or '-' => BidiClass.ES,
        '#' or '$' or '%' => BidiClass.ET,
        ',' or '.' or '/' or ':' => BidiClass.CS,
        ' ' or '\f' => BidiClass.WS,
        '\t' or '\v' or '\u001F' => BidiClass.S,
        '\n' or '\r' or '\u001C' or '\u001D' or '\u001E' => BidiClass.B,
        < ' ' or '\u007F' => BidiClass.BN,
        _ => BidiClass.ON,
    };

    // ===== a paragraph =====

    /// <summary>Resolve the levels of one paragraph. Returns its level.</summary>
    private static byte ResolveParagraph(
        string text, BidiClass[] classes, byte[] levels, int start, int end, FlowDirection? direction)
    {
        int[] matchingPdi = MatchIsolates(classes, start, end, out int[] matchingInitiator);

        // P2–P3
        byte paragraphLevel = direction switch
        {
            FlowDirection.RightToLeft => 1,
            FlowDirection.LeftToRight => 0,
            _ => FirstStrongIsRightToLeft(classes, start, end, matchingPdi) ? (byte)1 : (byte)0,
        };

        var types = new BidiClass[end - start];
        Array.Copy(classes, start, types, 0, end - start);

        ResolveExplicit(classes, types, levels, start, end, paragraphLevel, matchingPdi);

        // sos and eos compare the explicit levels: the implicit ones of a sequence
        // resolved earlier must not leak into its neighbour's
        byte[] explicitLevels = levels.AsSpan(start, end - start).ToArray();

        foreach (int[] sequence in IsolatingRunSequences(classes, levels, start, end, matchingPdi, matchingInitiator))
            ResolveSequence(text, classes, types, levels, explicitLevels, sequence, start, end, paragraphLevel);

        // the characters X9 removed take the level of what precedes them: they draw
        // nothing, and must not split a run in two
        for (int i = start; i < end; i++)
            if (IsRemoved(classes[i]))
                levels[i] = i > start ? levels[i - 1] : paragraphLevel;

        return paragraphLevel;
    }

    /// <summary>BD9: the PDI that closes each isolate initiator, and back.</summary>
    private static int[] MatchIsolates(BidiClass[] classes, int start, int end, out int[] matchingInitiator)
    {
        int length = end - start;
        var matchingPdi = new int[length];
        matchingInitiator = new int[length];

        Array.Fill(matchingPdi, -1);
        Array.Fill(matchingInitiator, -1);

        var open = new Stack<int>();

        for (int i = start; i < end; i++)
        {
            switch (classes[i])
            {
                case BidiClass.LRI or BidiClass.RLI or BidiClass.FSI:
                    open.Push(i);
                    break;

                case BidiClass.PDI when open.Count > 0:
                    int initiator = open.Pop();
                    matchingPdi[initiator - start] = i;
                    matchingInitiator[i - start] = initiator;
                    break;
            }
        }

        return matchingPdi;
    }

    /// <summary>P2: the first strong character, skipping isolates, says right to left.</summary>
    private static bool FirstStrongIsRightToLeft(BidiClass[] classes, int start, int end, int[] matchingPdi)
    {
        for (int i = start; i < end; i++)
        {
            switch (classes[i])
            {
                case BidiClass.L:
                    return false;

                case BidiClass.R or BidiClass.AL:
                    return true;

                case BidiClass.LRI or BidiClass.RLI or BidiClass.FSI:
                    int pdi = matchingPdi[i - start];
                    if (pdi < 0) return false;
                    i = pdi;
                    break;
            }
        }

        return false;
    }

    /// <summary>Whether an isolate's content starts right to left (for FSI).</summary>
    private static bool IsolateIsRightToLeft(BidiClass[] classes, int initiator, int end, int[] matchingPdi, int start)
    {
        int pdi = matchingPdi[initiator - start];
        int stop = pdi < 0 ? end : pdi;

        for (int i = initiator + 1; i < stop; i++)
        {
            switch (classes[i])
            {
                case BidiClass.L:
                    return false;

                case BidiClass.R or BidiClass.AL:
                    return true;

                case BidiClass.LRI or BidiClass.RLI or BidiClass.FSI:
                    int inner = matchingPdi[i - start];
                    if (inner < 0) return false;
                    i = inner;
                    break;
            }
        }

        return false;
    }

    private readonly record struct Status(byte Level, BidiClass Override, bool Isolate);

    /// <summary>X1–X8: explicit levels and directional overrides.</summary>
    private static void ResolveExplicit(
        BidiClass[] classes, BidiClass[] types, byte[] levels, int start, int end, byte paragraphLevel, int[] matchingPdi)
    {
        var stack = new Stack<Status>();
        stack.Push(new Status(paragraphLevel, BidiClass.ON, false));

        int overflowIsolates = 0, overflowEmbeddings = 0, validIsolates = 0;

        for (int i = start; i < end; i++)
        {
            BidiClass type = classes[i];
            Status current = stack.Peek();

            switch (type)
            {
                case BidiClass.RLE or BidiClass.LRE or BidiClass.RLO or BidiClass.LRO:
                {
                    bool rtl = type is BidiClass.RLE or BidiClass.RLO;
                    byte level = rtl ? NextOdd(current.Level) : NextEven(current.Level);

                    levels[i] = current.Level;

                    if (level <= MaxDepth && overflowIsolates == 0 && overflowEmbeddings == 0)
                    {
                        BidiClass over = type switch
                        {
                            BidiClass.RLO => BidiClass.R,
                            BidiClass.LRO => BidiClass.L,
                            _ => BidiClass.ON,
                        };

                        stack.Push(new Status(level, over, false));
                    }
                    else if (overflowIsolates == 0)
                    {
                        overflowEmbeddings++;
                    }

                    break;
                }

                case BidiClass.RLI or BidiClass.LRI or BidiClass.FSI:
                {
                    levels[i] = current.Level;

                    if (current.Override != BidiClass.ON)
                        types[i - start] = current.Override;

                    bool rtl = type == BidiClass.RLI ||
                               (type == BidiClass.FSI && IsolateIsRightToLeft(classes, i, end, matchingPdi, start));

                    byte level = rtl ? NextOdd(current.Level) : NextEven(current.Level);

                    if (level <= MaxDepth && overflowIsolates == 0 && overflowEmbeddings == 0)
                    {
                        validIsolates++;
                        stack.Push(new Status(level, BidiClass.ON, true));
                    }
                    else
                    {
                        overflowIsolates++;
                    }

                    break;
                }

                case BidiClass.PDI:
                {
                    if (overflowIsolates > 0)
                    {
                        overflowIsolates--;
                    }
                    else if (validIsolates > 0)
                    {
                        overflowEmbeddings = 0;

                        while (!stack.Peek().Isolate) stack.Pop();

                        stack.Pop();
                        validIsolates--;
                    }

                    current = stack.Peek();
                    levels[i] = current.Level;

                    if (current.Override != BidiClass.ON)
                        types[i - start] = current.Override;

                    break;
                }

                case BidiClass.PDF:
                {
                    if (overflowIsolates > 0)
                    {
                    }
                    else if (overflowEmbeddings > 0)
                    {
                        overflowEmbeddings--;
                    }
                    else if (!current.Isolate && stack.Count >= 2)
                    {
                        stack.Pop();
                    }

                    levels[i] = current.Level;
                    break;
                }

                case BidiClass.B:
                    levels[i] = paragraphLevel;
                    break;

                case BidiClass.BN:
                    levels[i] = current.Level;
                    break;

                default:
                    levels[i] = current.Level;

                    if (current.Override != BidiClass.ON)
                        types[i - start] = current.Override;

                    break;
            }
        }
    }

    private static byte NextOdd(byte level) => (byte)((level & 1) == 0 ? level + 1 : level + 2);

    private static byte NextEven(byte level) => (byte)((level & 1) == 0 ? level + 2 : level + 1);

    /// <summary>X9: what the rest of the algorithm passes by.</summary>
    private static bool IsRemoved(BidiClass type) => type is
        BidiClass.RLE or BidiClass.LRE or BidiClass.RLO or BidiClass.LRO or BidiClass.PDF or BidiClass.BN;

    /// <summary>X10: the level runs, chained across isolates into sequences.</summary>
    private static List<int[]> IsolatingRunSequences(
        BidiClass[] classes, byte[] levels, int start, int end, int[] matchingPdi, int[] matchingInitiator)
    {
        // the level runs, the removed characters left out
        var runs = new List<List<int>>();
        var runOf = new int[end - start];
        List<int>? currentRun = null;
        int currentLevel = -1;

        for (int i = start; i < end; i++)
        {
            if (IsRemoved(classes[i])) continue;

            if (currentRun is null || levels[i] != currentLevel)
            {
                currentRun = [];
                runs.Add(currentRun);
                currentLevel = levels[i];
            }

            currentRun.Add(i);
            runOf[i - start] = runs.Count - 1;
        }

        var sequences = new List<int[]>();

        foreach (List<int> run in runs)
        {
            int first = run[0];

            // a run opened by a matched PDI continues the sequence of its initiator
            if (classes[first] == BidiClass.PDI && matchingInitiator[first - start] >= 0)
                continue;

            var sequence = new List<int>(run);
            List<int> last = run;

            while (true)
            {
                int tail = last[^1];

                if (classes[tail] is not (BidiClass.LRI or BidiClass.RLI or BidiClass.FSI)) break;

                int pdi = matchingPdi[tail - start];
                if (pdi < 0) break;

                last = runs[runOf[pdi - start]];
                sequence.AddRange(last);
            }

            sequences.Add([.. sequence]);
        }

        return sequences;
    }

    /// <summary>W1–W7, N0–N2 and I1–I2 over one isolating run sequence.</summary>
    private static void ResolveSequence(
        string text, BidiClass[] classes, BidiClass[] paragraphTypes, byte[] levels, byte[] explicitLevels,
        int[] sequence, int start, int end, byte paragraphLevel)
    {
        int length = sequence.Length;
        byte level = explicitLevels[sequence[0] - start];

        // sos and eos: the higher of this level and the neighbour's
        int before = sequence[0] - 1;
        while (before >= start && IsRemoved(classes[before])) before--;

        byte previousLevel = before >= start ? explicitLevels[before - start] : paragraphLevel;

        int last = sequence[^1];
        byte nextLevel = paragraphLevel;

        if (classes[last] is not (BidiClass.LRI or BidiClass.RLI or BidiClass.FSI))
        {
            int after = last + 1;
            while (after < end && IsRemoved(classes[after])) after++;

            if (after < end) nextLevel = explicitLevels[after - start];
        }

        BidiClass sos = (Math.Max(previousLevel, level) & 1) != 0 ? BidiClass.R : BidiClass.L;
        BidiClass eos = (Math.Max(nextLevel, level) & 1) != 0 ? BidiClass.R : BidiClass.L;
        BidiClass embedding = (level & 1) != 0 ? BidiClass.R : BidiClass.L;

        var t = new BidiClass[length];

        for (int k = 0; k < length; k++)
            t[k] = paragraphTypes[sequence[k] - start];

        // W1: a nonspacing mark takes the type of what it marks
        for (int k = 0; k < length; k++)
        {
            if (t[k] != BidiClass.NSM) continue;

            t[k] = k == 0
                ? sos
                : t[k - 1] is BidiClass.LRI or BidiClass.RLI or BidiClass.FSI or BidiClass.PDI ? BidiClass.ON : t[k - 1];
        }

        // W2: a European number after Arabic letters is an Arabic number
        for (int k = 0; k < length; k++)
        {
            if (t[k] != BidiClass.EN) continue;

            for (int j = k - 1; j >= 0; j--)
            {
                if (t[j] is BidiClass.L or BidiClass.R) break;

                if (t[j] == BidiClass.AL)
                {
                    t[k] = BidiClass.AN;
                    break;
                }
            }
        }

        // W3
        for (int k = 0; k < length; k++)
            if (t[k] == BidiClass.AL) t[k] = BidiClass.R;

        // W4: one separator between two numbers of a kind joins them
        for (int k = 1; k < length - 1; k++)
        {
            if (t[k] == BidiClass.ES && t[k - 1] == BidiClass.EN && t[k + 1] == BidiClass.EN)
                t[k] = BidiClass.EN;
            else if (t[k] == BidiClass.CS && t[k - 1] == BidiClass.EN && t[k + 1] == BidiClass.EN)
                t[k] = BidiClass.EN;
            else if (t[k] == BidiClass.CS && t[k - 1] == BidiClass.AN && t[k + 1] == BidiClass.AN)
                t[k] = BidiClass.AN;
        }

        // W5: terminators next to a European number belong to it
        for (int k = 0; k < length; k++)
        {
            if (t[k] != BidiClass.ET) continue;

            int runEnd = k;
            while (runEnd < length && t[runEnd] == BidiClass.ET) runEnd++;

            bool number = (k > 0 && t[k - 1] == BidiClass.EN) || (runEnd < length && t[runEnd] == BidiClass.EN);

            if (number)
                for (int j = k; j < runEnd; j++) t[j] = BidiClass.EN;

            k = runEnd - 1;
        }

        // W6
        for (int k = 0; k < length; k++)
            if (t[k] is BidiClass.ES or BidiClass.ET or BidiClass.CS) t[k] = BidiClass.ON;

        // W7: a European number in left-to-right context is left to right
        for (int k = 0; k < length; k++)
        {
            if (t[k] != BidiClass.EN) continue;

            BidiClass strong = sos;

            for (int j = k - 1; j >= 0; j--)
            {
                if (t[j] is BidiClass.L or BidiClass.R)
                {
                    strong = t[j];
                    break;
                }
            }

            if (strong == BidiClass.L) t[k] = BidiClass.L;
        }

        ResolveBrackets(text, classes, sequence, t, sos, embedding);

        // N1–N2: neutrals between two strong types of one direction take it,
        // the others the embedding direction
        for (int k = 0; k < length; k++)
        {
            if (!IsNeutral(t[k])) continue;

            int runEnd = k;
            while (runEnd < length && IsNeutral(t[runEnd])) runEnd++;

            BidiClass leading = k == 0 ? sos : StrongForNeutrals(t[k - 1]);
            BidiClass trailing = runEnd == length ? eos : StrongForNeutrals(t[runEnd]);
            BidiClass resolved = leading == trailing ? leading : embedding;

            for (int j = k; j < runEnd; j++) t[j] = resolved;

            k = runEnd - 1;
        }

        // I1–I2
        for (int k = 0; k < length; k++)
        {
            int index = sequence[k];
            byte current = levels[index];

            if ((current & 1) == 0)
            {
                if (t[k] == BidiClass.R) levels[index] = (byte)(current + 1);
                else if (t[k] is BidiClass.AN or BidiClass.EN) levels[index] = (byte)(current + 2);
            }
            else if (t[k] is BidiClass.L or BidiClass.EN or BidiClass.AN)
            {
                levels[index] = (byte)(current + 1);
            }
        }
    }

    private static bool IsNeutral(BidiClass type) => type is
        BidiClass.B or BidiClass.S or BidiClass.WS or BidiClass.ON or
        BidiClass.LRI or BidiClass.RLI or BidiClass.FSI or BidiClass.PDI;

    /// <summary>Numbers count as right to left for the neutrals and brackets beside them.</summary>
    private static BidiClass StrongForNeutrals(BidiClass type) =>
        type == BidiClass.L ? BidiClass.L : BidiClass.R;

    /// <summary>The direction a resolved type gives a bracket pair, or ON for none.</summary>
    private static BidiClass StrongForBrackets(BidiClass type) => type switch
    {
        BidiClass.L => BidiClass.L,
        BidiClass.R or BidiClass.AL or BidiClass.EN or BidiClass.AN => BidiClass.R,
        _ => BidiClass.ON,
    };

    /// <summary>N0: a pair of brackets takes the direction of what it encloses — the
    /// embedding direction if anything inside has it, otherwise the other one when the
    /// context before the pair has it too.</summary>
    private static void ResolveBrackets(
        string text, BidiClass[] classes, int[] sequence, BidiClass[] t, BidiClass sos, BidiClass embedding)
    {
        List<(int Open, int Close)>? pairs = null;
        Stack<(char Close, int Position)>? open = null;

        for (int k = 0; k < sequence.Length; k++)
        {
            if (t[k] != BidiClass.ON) continue;

            char c = text[sequence[k]];

            if (OpeningBracket(c) is { } closing)
            {
                open ??= new Stack<(char, int)>();

                // BD16: too deep, and the search stops with the pairs found so far
                if (open.Count == MaxBracketDepth) break;

                open.Push((closing, k));
            }
            else if (IsClosingBracket(c) && open is { Count: > 0 })
            {
                foreach ((char expected, int position) in open)
                {
                    if (expected != c) continue;

                    while (open.Pop().Position != position) { }

                    (pairs ??= []).Add((position, k));
                    break;
                }
            }
        }

        if (pairs is null) return;

        pairs.Sort((a, b) => a.Open.CompareTo(b.Open));

        BidiClass opposite = embedding == BidiClass.L ? BidiClass.R : BidiClass.L;

        foreach ((int o, int c) in pairs)
        {
            bool sameInside = false, oppositeInside = false;

            for (int k = o + 1; k < c; k++)
            {
                BidiClass strong = StrongForBrackets(t[k]);

                if (strong == embedding) sameInside = true;
                else if (strong == opposite) oppositeInside = true;
            }

            BidiClass resolved;

            if (sameInside)
            {
                resolved = embedding;
            }
            else if (oppositeInside)
            {
                BidiClass context = sos;

                for (int k = o - 1; k >= 0; k--)
                {
                    BidiClass strong = StrongForBrackets(t[k]);

                    if (strong != BidiClass.ON)
                    {
                        context = strong;
                        break;
                    }
                }

                resolved = context == opposite ? opposite : embedding;
            }
            else
            {
                continue;
            }

            t[o] = t[c] = resolved;

            // marks on a bracket follow it
            for (int k = o + 1; k < t.Length && classes[sequence[k]] == BidiClass.NSM; k++) t[k] = resolved;
            for (int k = c + 1; k < t.Length && classes[sequence[k]] == BidiClass.NSM; k++) t[k] = resolved;
        }
    }

    /// <summary>The closing bracket of an opening one (BidiBrackets.txt), or null.</summary>
    private static char? OpeningBracket(char c) => c switch
    {
        '(' => ')',
        '[' => ']',
        '{' => '}',
        '༺' => '༻',
        '༼' => '༽',
        '᚛' => '᚜',
        '⁅' => '⁆',
        '⁽' => '⁾',
        '₍' => '₎',
        '⌈' => '⌉',
        '⌊' => '⌋',
        '〈' => '〉',
        '❨' => '❩',
        '❪' => '❫',
        '❬' => '❭',
        '❮' => '❯',
        '❰' => '❱',
        '❲' => '❳',
        '❴' => '❵',
        '⟅' => '⟆',
        '⟦' => '⟧',
        '⟨' => '⟩',
        '⟪' => '⟫',
        '⟬' => '⟭',
        '⟮' => '⟯',
        '⦃' => '⦄',
        '⦅' => '⦆',
        '⦇' => '⦈',
        '⦉' => '⦊',
        '⦋' => '⦌',
        '⸢' => '⸣',
        '⸤' => '⸥',
        '⸦' => '⸧',
        '⸨' => '⸩',
        '〈' => '〉',
        '《' => '》',
        '「' => '」',
        '『' => '』',
        '【' => '】',
        '〔' => '〕',
        '〖' => '〗',
        '〘' => '〙',
        '〚' => '〛',
        '﹙' => '﹚',
        '﹛' => '﹜',
        '﹝' => '﹞',
        '（' => '）',
        '［' => '］',
        '｛' => '｝',
        '｟' => '｠',
        '｢' => '｣',
        _ => null,
    };

    private static bool IsClosingBracket(char c) => c is
        ')' or ']' or '}' or '༻' or '༽' or '᚜' or '⁆' or '⁾' or '₎' or
        '⌉' or '⌋' or '〉' or '❩' or '❫' or '❭' or '❯' or '❱' or
        '❳' or '❵' or '⟆' or '⟧' or '⟩' or '⟫' or '⟭' or '⟯' or
        '⦄' or '⦆' or '⦈' or '⦊' or '⦌' or '⸣' or '⸥' or '⸧' or
        '⸩' or '〉' or '》' or '」' or '』' or '】' or '〕' or '〗' or
        '〙' or '〛' or '﹚' or '﹜' or '﹞' or '）' or '］' or '｝' or
        '｠' or '｣';
}
