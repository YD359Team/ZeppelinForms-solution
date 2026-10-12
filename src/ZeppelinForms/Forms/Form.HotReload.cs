using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms;

/// <summary>The form's part of Hot Reload, see <see cref="HotReload"/>.</summary>
public partial class Form
{
    /// <summary>An edit was applied by Hot Reload. Build again here what the constructor
    /// built — after this the framework remakes the factories' pages, restyles,
    /// measures and redraws the form.</summary>
    /// <example>
    /// <code>
    /// public MainForm()
    /// {
    ///     Title = "Orders";
    ///     Content = CreateContent();
    /// }
    ///
    /// protected override void OnHotReload(HotReloadEventArgs e) =&gt;
    ///     Content = CreateContent();
    /// </code>
    /// </example>
    /// <remarks>Does nothing by default. Elements have a hook of their own,
    /// <see cref="UIElement.OnHotReload"/>.</remarks>
    protected virtual void OnHotReload(HotReloadEventArgs e) { }

    internal void ApplyHotReload(HotReloadEventArgs e)
    {
        // the form first: content it builds again needs nothing more from the steps
        // below, and the old one isn't worth remaking
        HotReload.Run(() => OnHotReload(e), $"{GetType().Name}.OnHotReload");

        // the elements over a snapshot: a factory remade on the way detaches a subtree,
        // and what left the form is skipped
        var elements = new List<UIElement>();

        if (Content is not null)
            Walk(Content, elements.Add);

        foreach (UIElement overlay in _overlays.ToArray())
            Walk(overlay, elements.Add);

        foreach (UIElement element in elements)
        {
            if (!ReferenceEquals(element.FindOwner(), this)) continue;

            HotReload.Run(() => element.RaiseHotReload(e), $"{element.GetType().Name}.OnHotReload");
        }

        // the edited code shows only once it runs: theme rules, measuring, drawing
        RestyleAll();
        InvalidateMeasureTree();
        InvalidateVisual();
    }
}
