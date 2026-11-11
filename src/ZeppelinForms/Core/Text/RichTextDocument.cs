using System.Text;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Core.Text;

/// <summary>Plain text plus a list of formatting spans. Caret, selection and undo
/// live on the inner <see cref="TextDocument"/>; spans are shifted on every edit.</summary>
public sealed class RichTextDocument
{
    private readonly List<RichTextSpan> _spans = [];
    private string _knownText = string.Empty;

    public TextDocument Text { get; } = new() { IsMultiline = true };

    public IReadOnlyList<RichTextSpan> Spans => _spans;

    public string Content
    {
        get => Text.Text;
        set
        {
            Text.Text = value ?? string.Empty;
            _spans.Clear();
            _knownText = Text.Text;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? Changed;

    public RichTextDocument()
    {
        Text.Changed += OnTextChanged;
    }

    private void OnTextChanged(object? sender, EventArgs e)
    {
        // typing, paste, delete, undo and redo all land here. Diff against the
        // last known text so spans stay aligned even when the host did not call
        // NotifyEdit (history applies the splice itself).
        AdjustSpans(_knownText, Text.Text);
        _knownText = Text.Text;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void AdjustSpans(string oldText, string newText)
    {
        if (oldText == newText) return;

        int prefix = 0;
        int limit = Math.Min(oldText.Length, newText.Length);
        while (prefix < limit && oldText[prefix] == newText[prefix]) prefix++;

        int suffix = 0;
        int oldLeft = oldText.Length - prefix;
        int newLeft = newText.Length - prefix;
        while (suffix < oldLeft && suffix < newLeft &&
               oldText[oldText.Length - 1 - suffix] == newText[newText.Length - 1 - suffix])
            suffix++;

        int removed = oldText.Length - prefix - suffix;
        int inserted = newText.Length - prefix - suffix;
        if (removed != 0 || inserted != 0)
            NotifyEdit(prefix, removed, inserted);
    }

    /// <summary>Apply <paramref name="style"/> to <c>[start, start+length)</c>,
    /// splitting and merging spans so neighbours with the same look stay one.</summary>
    public void ApplyStyle(int start, int length, RichTextStyle style)
    {
        if (length <= 0) return;

        int end = Math.Min(start + length, Text.Text.Length);
        start = Math.Clamp(start, 0, Text.Text.Length);
        length = end - start;
        if (length <= 0) return;

        // collect covering spans, then replace the range with one styled span
        var next = new List<RichTextSpan>();

        foreach (RichTextSpan span in _spans)
        {
            if (span.End <= start || span.Start >= end)
            {
                next.Add(span);
                continue;
            }

            if (span.Start < start)
                next.Add(new RichTextSpan(span.Start, start - span.Start, span.Style));

            if (span.End > end)
                next.Add(new RichTextSpan(end, span.End - end, span.Style));
        }

        next.Add(new RichTextSpan(start, length, style));
        next.Sort((a, b) => a.Start.CompareTo(b.Start));
        _spans.Clear();
        _spans.AddRange(MergeAdjacent(next));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ApplyStyleToSelection(RichTextStyle style)
    {
        if (Text.SelectionLength > 0)
            ApplyStyle(Text.SelectionStart, Text.SelectionLength, style);
    }

    public RichTextStyle GetStyleAt(int index)
    {
        foreach (RichTextSpan span in _spans)
        {
            if (index >= span.Start && index < span.End)
                return span.Style;
        }

        return RichTextStyle.Empty;
    }

    /// <summary>Build <see cref="TextRun"/>s for a slice of the document.</summary>
    public List<TextRun> BuildRuns(int start, int length, Font baseFont, Color baseColor)
    {
        string text = Text.Text;
        int end = Math.Min(start + length, text.Length);
        start = Math.Clamp(start, 0, text.Length);
        var runs = new List<TextRun>();

        if (start >= end)
            return runs;

        int cursor = start;

        while (cursor < end)
        {
            RichTextStyle style = GetStyleAt(cursor);
            int runEnd = cursor + 1;

            while (runEnd < end && GetStyleAt(runEnd).Equals(style))
                runEnd++;

            runs.Add(style.ToRun(text[cursor..runEnd], baseFont, baseColor));
            cursor = runEnd;
        }

        return runs;
    }

    /// <summary>Shift spans after an insertion or deletion at <paramref name="position"/>.</summary>
    public void NotifyEdit(int position, int removedLength, int insertedLength)
    {
        int delta = insertedLength - removedLength;
        int removeEnd = position + removedLength;

        for (int i = _spans.Count - 1; i >= 0; i--)
        {
            RichTextSpan span = _spans[i];

            if (span.End <= position)
                continue;

            if (span.Start >= removeEnd)
            {
                span.Start += delta;
                continue;
            }

            // overlaps the edit: trim or drop
            int newStart = Math.Min(span.Start, position);
            int newEnd = Math.Max(span.End - removedLength, position) + insertedLength;

            if (span.Start >= position && span.End <= removeEnd)
            {
                _spans.RemoveAt(i);
                continue;
            }

            span.Start = newStart;
            span.Length = Math.Max(0, newEnd - newStart);

            if (span.Length == 0)
                _spans.RemoveAt(i);
        }
    }

    public string ToPlainText() => Text.Text;

    /// <summary>A simple markup dump: <c>**bold**</c>, <c>*italic*</c>, not a full format.</summary>
    public string ToSimpleMarkup()
    {
        string text = Text.Text;
        if (_spans.Count == 0) return text;

        var sb = new StringBuilder(text.Length + 32);
        int cursor = 0;

        foreach (RichTextSpan span in _spans.OrderBy(s => s.Start))
        {
            if (span.Start > cursor)
                sb.Append(text, cursor, span.Start - cursor);

            string piece = text.Substring(span.Start, Math.Min(span.Length, text.Length - span.Start));
            if (span.Style.Bold) sb.Append("**");
            if (span.Style.Italic) sb.Append('*');
            sb.Append(piece);
            if (span.Style.Italic) sb.Append('*');
            if (span.Style.Bold) sb.Append("**");
            cursor = span.End;
        }

        if (cursor < text.Length)
            sb.Append(text, cursor, text.Length - cursor);

        return sb.ToString();
    }

    private static List<RichTextSpan> MergeAdjacent(List<RichTextSpan> spans)
    {
        if (spans.Count == 0) return spans;

        var merged = new List<RichTextSpan> { spans[0] };

        for (int i = 1; i < spans.Count; i++)
        {
            RichTextSpan last = merged[^1];
            RichTextSpan cur = spans[i];

            if (last.End == cur.Start && last.Style == cur.Style)
                last.Length += cur.Length;
            else
                merged.Add(cur);
        }

        return merged;
    }
}
