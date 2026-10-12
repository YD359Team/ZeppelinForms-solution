namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>The element's part of Hot Reload, see <see cref="HotReload"/>.</summary>
public abstract partial class UIElement
{
    /// <summary>An edit was applied by Hot Reload. A control that builds its children in
    /// its constructor builds them again here. Called for every element of an open
    /// form, after <see cref="Form.OnHotReload"/>; the framework then restyles,
    /// measures and redraws the form.</summary>
    protected virtual void OnHotReload(HotReloadEventArgs e) { }

    /// <summary>The framework's own reaction, before the element's hook: it must not
    /// depend on an override remembering to call the base.</summary>
    private protected virtual void HotReloadCore(HotReloadEventArgs e) { }

    internal void RaiseHotReload(HotReloadEventArgs e)
    {
        HotReloadCore(e);
        OnHotReload(e);
    }
}
