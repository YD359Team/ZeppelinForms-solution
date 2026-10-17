namespace ZeppelinForms.Forms.Enums;

/// <summary>Which sides of a window follow its content, as WPF's SizeToContent.</summary>
public enum SizeToContent
{
    /// <summary>The window is as large as <see cref="Form.Size"/> says.</summary>
    None,

    /// <summary>The width follows the content, the height is Size's.</summary>
    Width,

    /// <summary>The height follows the content laid out at Size's width.</summary>
    Height,

    /// <summary>Both follow the content; MaximumSize.Width is where text wraps.</summary>
    WidthAndHeight,
}