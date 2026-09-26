namespace ZeppelinForms.Forms.Enums;

public enum Overflow
{
    Visible,   // content is neither clipped nor scrolled
    Hidden,    // clipped, no scrolling
    Scroll,    // the bar is always shown
    Auto,      // the bar appears when the content doesn't fit
}