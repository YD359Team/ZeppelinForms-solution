using ZeppelinForms.Core.Text;
using ZeppelinForms.Drawing;

namespace ZeppelinForms.Forms.Controls.Text;

/// <summary>
/// Breaks a line of text into rows no wider than a width, and shortens a row that
/// doesn't fit with an ellipsis. One implementation for the text box and the label.
/// </summary>
/// <remarks>
/// The breaks go after spaces, so words stay whole; a word wider than the row is
/// broken where it overflows. The spaces at a break hang at the end of the row
/// before it, as in every editor: they are part of the text the caret walks, and a
/// label simply doesn't count them when it aligns the row.
/// </remarks>
internal static class TextWrapper
{
    public const string Ellipsis = "…";

    private static bool IsSpace(char c) => c is ' ' or '\t';

    /// <summary>The rows of one line — a text without '\n' — as pieces of it: where
    /// each starts and how long it is. A line that fits is one row; an empty one
    /// is one empty row.</summary>
    public static List<(int Start, int Length)> Wrap(string line, float width, Font font)
    {
        var rows = new List<(int Start, int Length)>();

        if (line.Length == 0 || !(width > 0) || !float.IsFinite(width))
        {
            rows.Add((0, line.Length));
            return rows;
        }

        ITextMeasurer measurer = TextMeasurer.Current;
        int start = 0;

        while (start < line.Length)
        {
            string rest = line[start..];

            if (measurer.MeasureTextWidth(rest, rest.Length, font) <= width)
            {
                rows.Add((start, rest.Length));
                break;
            }

            // the longest piece that fits, by a binary search over the character
            // boundaries: widths only grow, and measuring every prefix would be quadratic
            int end = LongestFitting(rest, width, font);

            // at least one character a row, however narrow the room
            if (end == 0) end = TextElements.Next(rest, 0);

            // break after the last space of the piece, so words stay whole; a word
            // wider than the row is broken where it overflows
            if (end < rest.Length && !IsSpace(rest[end]))
            {
                int space = rest.LastIndexOfAny([' ', '\t'], end - 1, end);

                if (space >= 0) end = space + 1;
            }

            // spaces at the break hang at the end of the row, as in every editor
            while (end < rest.Length && IsSpace(rest[end])) end++;

            rows.Add((start, end));
            start += end;
        }

        return rows;
    }

    /// <summary>The row as it fits <paramref name="width"/>: itself when it fits,
    /// otherwise its longest beginning that fits with an ellipsis after it — cut
    /// anywhere, or, with <paramref name="atWord"/>, after the last whole word.</summary>
    public static string Ellipsize(string row, float width, Font font, bool atWord)
    {
        ITextMeasurer measurer = TextMeasurer.Current;

        if (measurer.MeasureTextWidth(row, row.Length, font) <= width) return row;

        float room = width - measurer.MeasureTextWidth(Ellipsis, Ellipsis.Length, font);
        if (room <= 0) return Ellipsis;

        int end = LongestFitting(row, room, font);

        if (atWord)
        {
            int space = row.LastIndexOfAny([' ', '\t'], Math.Max(0, end - 1), end);
            if (space > 0) end = space;
        }

        return row[..end].TrimEnd() + Ellipsis;
    }

    /// <summary>How many characters from the start fit the width, at a character
    /// boundary: an emoji or a letter with its marks is never cut in two.</summary>
    private static int LongestFitting(string text, float width, Font font)
    {
        ITextMeasurer measurer = TextMeasurer.Current;
        List<int> bounds = [.. TextElements.Boundaries(text)];

        int fits = 0;
        int low = 1, high = bounds.Count - 1;

        while (low <= high)
        {
            int middle = (low + high) / 2;

            if (measurer.MeasureTextWidth(text, bounds[middle], font) <= width)
            {
                fits = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return bounds[fits];
    }
}
