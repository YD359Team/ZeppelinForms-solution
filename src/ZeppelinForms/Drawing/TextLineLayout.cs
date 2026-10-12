using ZeppelinForms.Core.Text;

namespace ZeppelinForms.Drawing;

/// <summary>A piece of a laid-out line in one direction, in visual order.</summary>
/// <param name="Start">Its first UTF-16 index in the line's text.</param>
/// <param name="Length">Its length in UTF-16 units.</param>
/// <param name="IsRightToLeft">Whether its characters run right to left.</param>
/// <param name="X">Where it starts from the line's left edge.</param>
/// <param name="Width">How wide it is.</param>
public readonly record struct TextLineRun(int Start, int Length, bool IsRightToLeft, float X, float Width);

/// <summary>
/// One line of text as it stands on the screen: its runs in visual order, and the box
/// of every character — what a caret, a selection and a click on the line are placed by.
/// </summary>
/// <remarks>
/// <para>
/// In a line that mixes directions a logical position doesn't map to one x by adding
/// widths: "abc אבג" puts the Hebrew word's first letter at its right end. The layout
/// answers from the characters' boxes instead, which the measurer gets from the shaper
/// — ligatures and combining marks included.
/// </para>
/// <para>
/// A caret at a position stands at the leading edge of the character after it: the left
/// edge of a left-to-right character, the right edge of a right-to-left one. At the end
/// of the line it stands at the trailing edge of the last character.
/// </para>
/// </remarks>
public sealed class TextLineLayout
{
    private readonly float[] _left;
    private readonly float[] _right;
    private readonly bool[] _rightToLeft;

    /// <param name="text">The line.</param>
    /// <param name="isRightToLeft">The direction of its paragraph.</param>
    /// <param name="runs">Its runs in visual order, left to right.</param>
    /// <param name="characterLefts">The left edge of every character's box. A character
    /// that shares a glyph with others — a ligature, a combining mark — gets a part of it.</param>
    /// <param name="characterRights">The right edge of every character's box.</param>
    public TextLineLayout(
        string text, bool isRightToLeft, IReadOnlyList<TextLineRun> runs,
        float[] characterLefts, float[] characterRights)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(characterLefts);
        ArgumentNullException.ThrowIfNull(characterRights);

        if (characterLefts.Length != text.Length || characterRights.Length != text.Length)
            throw new ArgumentException("A box is needed for every character of the text.");

        Text = text;
        IsRightToLeft = isRightToLeft;
        Runs = runs;
        _left = characterLefts;
        _right = characterRights;
        _rightToLeft = new bool[text.Length];

        float width = 0;

        foreach (TextLineRun run in runs)
        {
            width = Math.Max(width, run.X + run.Width);

            for (int i = run.Start; i < run.Start + run.Length && i < text.Length; i++)
                _rightToLeft[i] = run.IsRightToLeft;
        }

        Width = width;
    }

    /// <summary>An empty line.</summary>
    public static TextLineLayout Empty(bool isRightToLeft = false) =>
        new(string.Empty, isRightToLeft, [], [], []);

    public string Text { get; }

    /// <summary>Whether the line's paragraph runs right to left.</summary>
    public bool IsRightToLeft { get; }

    /// <summary>The runs in visual order, left to right.</summary>
    public IReadOnlyList<TextLineRun> Runs { get; }

    public float Width { get; }

    /// <summary>Whether a character runs right to left.</summary>
    public bool IsCharacterRightToLeft(int index) => _rightToLeft[index];

    /// <summary>The box of a character: its left and right edge.</summary>
    public (float Left, float Right) GetCharacterBounds(int index) => (_left[index], _right[index]);

    /// <summary>Where the caret stands at a position, from the line's left edge.</summary>
    public float CaretX(int index)
    {
        int length = Text.Length;

        if (length == 0) return 0;

        index = Math.Clamp(index, 0, length);

        if (index < length)
            return _rightToLeft[index] ? _right[index] : _left[index];

        int last = length - 1;
        return _rightToLeft[last] ? _left[last] : _right[last];
    }

    /// <summary>The caret position nearest to an x: the edge of the character under it
    /// that is nearer, between visible characters only — never inside a surrogate pair,
    /// a combining sequence or an emoji.</summary>
    public int IndexAt(float x)
    {
        int length = Text.Length;

        if (length == 0) return 0;

        int best = 0;
        float bestDistance = float.MaxValue;

        foreach (int position in TextElements.Boundaries(Text))
        {
            float distance = Math.Abs(CaretX(position) - x);

            if (distance < bestDistance)
            {
                best = position;
                bestDistance = distance;
            }
        }

        // a click inside a character's box picks the nearer of its two edges, which
        // in a mixed line are not the carets of neighbouring positions — check the box
        foreach ((int start, int end) in Clusters())
        {
            (float left, float right) = ClusterBounds(start, end);

            if (x < left || x >= right) continue;

            bool leftHalf = x < (left + right) / 2f;
            bool rtl = _rightToLeft[start];

            return leftHalf != rtl ? start : end;
        }

        return best;
    }

    /// <summary>The spans a selection of a logical range covers, left to right: one in
    /// a line of one direction, several where the range crosses runs.</summary>
    public IReadOnlyList<(float Left, float Right)> SelectionBounds(int start, int end)
    {
        start = Math.Clamp(start, 0, Text.Length);
        end = Math.Clamp(end, 0, Text.Length);

        if (start > end) (start, end) = (end, start);
        if (start == end) return [];

        var boxes = new List<(float Left, float Right)>(end - start);

        for (int i = start; i < end; i++)
            if (_right[i] > _left[i])
                boxes.Add((_left[i], _right[i]));

        boxes.Sort((a, b) => a.Left.CompareTo(b.Left));

        var merged = new List<(float Left, float Right)>();

        foreach ((float left, float right) in boxes)
        {
            // touching boxes are one span; half a pixel absorbs rounding of the advances
            if (merged.Count > 0 && left <= merged[^1].Right + 0.5f)
                merged[^1] = (merged[^1].Left, Math.Max(merged[^1].Right, right));
            else
                merged.Add((left, right));
        }

        return merged;
    }

    /// <summary>The position a caret goes to when moved one visible character to the
    /// right or the left on the screen — across runs as they are drawn, not as they are
    /// stored. Null: there is nothing further on this line.</summary>
    public int? MoveVisually(int index, bool right)
    {
        float current = CaretX(index);
        int? best = null;
        float bestX = 0;

        foreach (int position in TextElements.Boundaries(Text))
        {
            if (position == index) continue;

            float x = CaretX(position);

            bool beyond = right ? x > current + 0.01f : x < current - 0.01f;
            if (!beyond) continue;

            bool nearer = best is null || (right ? x < bestX : x > bestX);

            if (nearer)
            {
                best = position;
                bestX = x;
            }
        }

        return best;
    }

    /// <summary>The visible characters, as ranges of UTF-16 units.</summary>
    private IEnumerable<(int Start, int End)> Clusters()
    {
        int position = 0;

        while (position < Text.Length)
        {
            int next = TextElements.Next(Text, position);
            yield return (position, next);
            position = next;
        }
    }

    private (float Left, float Right) ClusterBounds(int start, int end)
    {
        float left = float.MaxValue, right = float.MinValue;

        for (int i = start; i < end; i++)
        {
            left = Math.Min(left, _left[i]);
            right = Math.Max(right, _right[i]);
        }

        return (left, right);
    }
}
