using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms.Controls;

/// <summary>
/// The surface of a flyout: the theme's surface color, a hairline edge, a shadow and
/// the rounding the theme gives to what floats over the content.
/// </summary>
/// <remarks>
/// Form.ShowFlyout shows its content as it is — a menu or a picker draws a surface
/// of its own. Content without one, a plain panel, goes into a FlyoutPresenter;
/// shown bare, it had no background, and its text ran into the form under it.
/// </remarks>
public class FlyoutPresenter : Border
{
    public FlyoutPresenter()
    {
    }

    public FlyoutPresenter(UIElement child) : base(child)
    {
    }
}