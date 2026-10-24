using ZeppelinForms.Drawing;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms.Controls.Navigation;

/// <summary>
/// One view inside PageControl. The content is created once and survives
/// switches — typed text and scroll position are kept.
/// </summary>
public class Page : DecoratedWrapControl
{
    private Func<UIElement>? _factory;
    private bool _built;

    public string? Title { get; set; }
    public string? IconPathData { get; set; }

    /// <summary>Lazy creation: the content is built on first show.</summary>
    public Func<UIElement>? ContentFactory
    {
        get => _factory;
        set
        {
            _factory = value;
            _built = false;
        }
    }

    public event EventHandler? Appearing;
    public event EventHandler? Disappearing;

    /// <summary>The content has already been created. PageControl looks at this
    /// when preparing pages in advance.</summary>
    internal bool IsBuilt => _built;

    internal void EnsureBuilt()
    {
        if (_built || _factory is null) return;

        _built = false;
        try
        {
            Child = _factory();
            _built = true;
        }
        catch
        {
            throw;
        }
    }

    /// <summary>Hot Reload: content made by a factory whose assembly changed is made
    /// again — at once on the page shown, on first show for the others.</summary>
    private protected override void HotReloadCore(HotReloadEventArgs e)
    {
        base.HotReloadCore(e);

        if (!_built || _factory is null || !e.Changed(_factory)) return;

        _built = false;
        Child = null;

        if (IsVisible) EnsureBuilt();
    }

    internal void RaiseAppearing()
    {
        EnsureBuilt();
        Appearing?.Invoke(this, EventArgs.Empty);
    }

    internal void RaiseDisappearing() => Disappearing?.Invoke(this, EventArgs.Empty);
}