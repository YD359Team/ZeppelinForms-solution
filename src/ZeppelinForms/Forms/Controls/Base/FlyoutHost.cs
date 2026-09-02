using ZeppelinForms.Forms.Layout;

namespace ZeppelinForms.Forms.Controls.Base;

/// <summary>
/// Помощник для контролов, открывающих флаут: следит за его закрытием
/// извне, чтобы контрол не считал закрытый флаут открытым.
/// </summary>
public sealed class FlyoutHost
{
    private readonly UIElement _owner;
    private UIElement? _content;
    private Form? _form;

    public FlyoutHost(UIElement owner) => _owner = owner;

    public bool IsOpen => _content is not null;

    /// <summary>Вызывается, когда флаут закрылся любым способом.</summary>
    public event EventHandler? Closed;

    public bool Toggle(Func<UIElement> createContent, FlyoutPlacement placement = FlyoutPlacement.Bottom)
    {
        if (IsOpen)
        {
            Close();
            return false;
        }

        Open(createContent(), placement);
        return true;
    }

    public void Open(UIElement content, FlyoutPlacement placement = FlyoutPlacement.Bottom)
    {
        Form? form = _owner.FindOwner();
        if (form is null) return;

        Close();

        _form = form;
        _content = content;

        // подписка живёт ровно столько, сколько открыт флаут
        form.FlyoutClosed += OnFlyoutClosed;
        form.ShowFlyout(_owner, content, placement);
    }

    public void Close()
    {
        if (_content is null) return;

        UIElement content = _content;
        Form? form = _form;

        Unsubscribe();
        form?.CloseFlyout(content);
    }

    private void OnFlyoutClosed(object? sender, UIElement closed)
    {
        if (!ReferenceEquals(closed, _content)) return;

        Unsubscribe();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void Unsubscribe()
    {
        if (_form is not null)
            _form.FlyoutClosed -= OnFlyoutClosed;

        _form = null;
        _content = null;
    }
}