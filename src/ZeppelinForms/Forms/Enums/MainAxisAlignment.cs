namespace ZeppelinForms.Forms.Enums;

/// <summary>How to distribute children along the panel's main axis.</summary>
public enum MainAxisAlignment
{
    Start,
    Center,
    End,
    /// <summary>Equal gaps, the outermost children pressed to the edges.</summary>
    SpaceBetween,
    /// <summary>Equal gaps, half-size gaps at the edges.</summary>
    SpaceAround,
    /// <summary>All gaps, including the outer ones, are equal.</summary>
    SpaceEvenly,
}