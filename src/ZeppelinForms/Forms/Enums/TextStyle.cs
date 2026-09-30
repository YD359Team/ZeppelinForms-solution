namespace ZeppelinForms.Forms.Enums;

/// <summary>A step of the theme's type ramp. The element takes the size and weight
/// of the step from <see cref="Theming.TypeRamp"/>, the family — from its font.</summary>
/// <remarks>The names are those of the Fluent 2 type ramp.</remarks>
public enum TextStyle
{
    /// <summary>No style: the element's font as it is set or inherited.</summary>
    None,

    /// <summary>Small secondary text: hints, annotations, timestamps.</summary>
    Caption,

    /// <summary>Regular text.</summary>
    Body,

    /// <summary>Regular text with emphasis.</summary>
    BodyStrong,

    /// <summary>Larger text for a lead paragraph.</summary>
    BodyLarge,

    /// <summary>The heading of a section.</summary>
    Subtitle,

    /// <summary>The heading of a page.</summary>
    Title,

    /// <summary>A large page heading.</summary>
    TitleLarge,

    /// <summary>The largest text: a hero line, a big number.</summary>
    Display,
}