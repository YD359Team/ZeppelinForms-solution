using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Forms;

/// <summary>A window as large as what it shows: a message box, an input box, a dialog
/// of one's own whose text and buttons decide its size.</summary>
public partial class Form
{
    /// <summary>Which sides of the window follow its content. With it, <see cref="Size"/>
    /// is the client area on every platform — Windows adds the frame and the title bar
    /// around it, as the other platforms always have. <see cref="MinimumSize"/> and
    /// <see cref="MaximumSize"/> bound the result, and MaximumSize.Width is the width
    /// text wraps at. Applied when the window is created by App.Run,
    /// <see cref="Show(Form)"/> or a dialog.</summary>
    public SizeToContent SizeToContent { get; set; }

    /// <summary>Measure the content and take its size as <see cref="Size"/>.</summary>
    /// <remarks>Before the window exists: the theme is already applied to the tree
    /// (AttachTree does that when Content is set), so the fonts and padding are the
    /// ones it will be drawn with.</remarks>
    internal void ApplySizeToContent()
    {
        if (SizeToContent == SizeToContent.None || Content is null) return;

        bool width = SizeToContent is SizeToContent.Width or SizeToContent.WidthAndHeight;
        bool height = SizeToContent is SizeToContent.Height or SizeToContent.WidthAndHeight;

        // a width that follows the content is offered up to the maximum, where text
        // wraps; a fixed one is offered as it is
        float availableWidth = width
            ? (float.IsFinite(MaximumSize.Width) ? MaximumSize.Width : float.PositiveInfinity)
            : Size.Width;

        float availableHeight = height ? float.PositiveInfinity : Size.Height;

        Content.Measure(new Size(availableWidth, availableHeight));
        Size desired = Content.DesiredSize;

        float w = width ? Bound(desired.Width, MinimumSize.Width, MaximumSize.Width) : Size.Width;
        float h = height ? Bound(desired.Height, MinimumSize.Height, MaximumSize.Height) : Size.Height;

        // whole units up: a fraction cut off clips the last pixel of a border
        Size = new Size(MathF.Ceiling(w), MathF.Ceiling(h));
    }

    /// <summary>A bound that is not set (NaN, as Size.Auto has it) doesn't bound.</summary>
    private static float Bound(float value, float min, float max)
    {
        if (float.IsFinite(min)) value = Math.Max(value, min);
        if (float.IsFinite(max)) value = Math.Min(value, max);

        return value;
    }
}