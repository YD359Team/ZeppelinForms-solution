using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;

namespace ZeppelinForms.Accessibility;

/// <summary>A form: the root of its tree. A window, or a dialog while it is shown
/// as one; its children are the content and, above it, the open overlays — menus,
/// drop-downs, flyouts, toasts.</summary>
public class FormPeer : AccessibilityPeer
{
    private readonly Form _form;

    public FormPeer(Form form) => _form = form;

    public override AccessibilityRole Role =>
        _form.IsDialog ? AccessibilityRole.Dialog : AccessibilityRole.Window;

    public override string Name => _form.Title ?? string.Empty;

    public override AccessibilityStates States =>
        _form.IsDialog ? AccessibilityStates.Modal : AccessibilityStates.None;

    public override AccessibilityPeer? Parent => null;

    public override IReadOnlyList<AccessibilityPeer> Children
    {
        get
        {
            var children = new List<AccessibilityPeer>();

            if (_form.Content is { IsVisible: true, IsAccessibilityHidden: false } content &&
                content.GetAccessibilityPeer() is { } contentPeer)
            {
                children.Add(contentPeer);
            }

            foreach (Forms.Controls.Base.UIElement overlay in _form.Overlays)
            {
                if (overlay.IsVisible && !overlay.IsAccessibilityHidden &&
                    overlay.GetAccessibilityPeer() is { } overlayPeer)
                {
                    children.Add(overlayPeer);
                }
            }

            return children;
        }
    }

    public override Rectangle Bounds => new(Point.Empty, _form.ClientSize);

    public override Form? Form => _form;
}