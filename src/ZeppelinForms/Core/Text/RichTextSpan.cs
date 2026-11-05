namespace ZeppelinForms.Core.Text;

/// <summary>A contiguous range of characters sharing one <see cref="RichTextStyle"/>.</summary>
public sealed class RichTextSpan
{
    public RichTextSpan(int start, int length, RichTextStyle style)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        Start = start;
        Length = length;
        Style = style;
    }

    public int Start { get; internal set; }
    public int Length { get; internal set; }
    public int End => Start + Length;
    public RichTextStyle Style { get; internal set; }

    public RichTextSpan Clone() => new(Start, Length, Style);
}
