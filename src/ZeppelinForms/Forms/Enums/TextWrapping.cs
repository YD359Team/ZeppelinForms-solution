namespace ZeppelinForms.Forms.Enums;

/// <summary>Whether a caption breaks into rows at the edge of its room. Like WPF's
/// TextWrapping and CSS's white-space.</summary>
public enum TextWrapping : byte
{
    /// <summary>One row per line of the text; what doesn't fit is cut off, or
    /// trimmed by <see cref="TextTrimming"/>.</summary>
    NoWrap,

    /// <summary>Rows break after spaces, so words stay whole; a word wider than the
    /// room is broken where it overflows.</summary>
    Wrap,
}
