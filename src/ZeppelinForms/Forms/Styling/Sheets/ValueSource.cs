namespace ZeppelinForms.Forms.Styling;

/// <summary>Where the current value of a styled property came from — the steps of
/// the ladder of sources, weakest first.</summary>
public enum ValueSource
{
    /// <summary>Nobody set it: the property's default.</summary>
    Default,

    /// <summary>The control's own default, set in its constructor.</summary>
    ControlDefault,

    /// <summary>A rule of the current theme.</summary>
    Theme,

    /// <summary>A style: of the application, the form or an element.</summary>
    Style,

    /// <summary>A binding.</summary>
    Binding,

    /// <summary>An assignment from code.</summary>
    Local,
}