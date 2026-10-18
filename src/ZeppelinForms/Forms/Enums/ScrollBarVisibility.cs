namespace ZeppelinForms.Forms.Enums;

/// <summary>When a control that scrolls its own content shows a scroll bar, as WPF has
/// it for a text box. The content scrolls with the wheel and the keyboard whatever
/// the bar does.</summary>
public enum ScrollBarVisibility
{
    /// <summary>The bar appears when the content doesn't fit.</summary>
    Auto,

    /// <summary>The bar is always there, its thumb filling it while everything fits.</summary>
    Visible,

    /// <summary>No bar; the content still scrolls.</summary>
    Hidden,
}