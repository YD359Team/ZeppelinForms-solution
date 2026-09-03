using ZeppelinForms.Drawing;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms.Controls.Navigation;

/// <summary>
/// Одно представление внутри PageControl. Содержимое создаётся один раз
/// и переживает переключения — введённый текст и прокрутка сохраняются.
/// </summary>
public class Page : WrapControl
{
    private Func<UIElement>? _factory;
    private bool _built;

    public string? Title { get; set; }
    public string? IconPathData { get; set; }

    /// <summary>Ленивое создание: содержимое строится при первом показе.</summary>
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

    public override void Draw(Graphics g) { }

    internal void EnsureBuilt()
    {
        if (_built || _factory is null) return;

        _built = true;
        Child = _factory();
    }

    internal void RaiseAppearing()
    {
        EnsureBuilt();
        Appearing?.Invoke(this, EventArgs.Empty);
    }

    internal void RaiseDisappearing() => Disappearing?.Invoke(this, EventArgs.Empty);
}
