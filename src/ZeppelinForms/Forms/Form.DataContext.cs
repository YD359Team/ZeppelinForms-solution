using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms;

/// <summary>The form's data context: the view model of the whole form.</summary>
public partial class Form
{
    /// <summary>
    /// The object the bindings of the form's elements start from when they have no data
    /// context of their own: the form's view model. The content and the overlays inherit it.
    /// </summary>
    /// <example>
    /// <code>
    /// var form = new OrderForm { DataContext = new OrderViewModel(order) };
    /// </code>
    /// </example>
    public object? DataContext
    {
        get;
        set
        {
            if (ReferenceEquals(field, value)) return;

            field = value;

            if (Content is { } content)
                UIElement.DataContextChangedBelow(content, isStart: false);

            foreach (UIElement overlay in _overlays.ToArray())
                UIElement.DataContextChangedBelow(overlay, isStart: false);
        }
    }
}
