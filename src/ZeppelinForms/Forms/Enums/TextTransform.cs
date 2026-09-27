namespace ZeppelinForms.Forms.Enums;

/// <summary>How a control's caption is cased when shown. Like text-transform in CSS:
/// the text itself is not changed, only how it is drawn and measured.</summary>
public enum TextTransform
{
    /// <summary>As written.</summary>
    Normal,

    /// <summary>All letters in upper case, by the rules of the current culture.</summary>
    UpperCase,

    /// <summary>All letters in lower case, by the rules of the current culture.</summary>
    LowerCase,
}