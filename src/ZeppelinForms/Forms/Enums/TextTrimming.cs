namespace ZeppelinForms.Forms.Enums;

/// <summary>How a caption that doesn't fit shows that it goes on. Like WPF's
/// TextTrimming and CSS's text-overflow.</summary>
public enum TextTrimming : byte
{
    /// <summary>Cut off at the edge.</summary>
    None,

    /// <summary>Shortened to the last character that fits with "…" after it.</summary>
    CharacterEllipsis,

    /// <summary>Shortened to the last whole word that fits with "…" after it.</summary>
    WordEllipsis,
}
