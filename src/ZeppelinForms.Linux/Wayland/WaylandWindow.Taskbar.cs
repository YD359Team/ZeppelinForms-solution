using ZeppelinForms.Forms;
using ZeppelinForms.Linux.Common;

namespace ZeppelinForms.Linux.Wayland;

/// <summary>The launcher entry's progress and count, and demanding attention.</summary>
internal sealed partial class WaylandWindow : ITaskbarWindow
{
    public void SetTaskbarProgress(TaskbarProgressState state, double value) =>
        LauncherEntry.SetProgress(state, value);

    public void SetTaskbarBadge(int? count) => LauncherEntry.SetCount(count);

    /// <summary>Wayland has no "demands attention" a client sets. What it has is
    /// xdg-activation: a window asking to be activated with a token that no input of the
    /// user's stands behind is, by the protocol, one the compositor "may mark as demanding
    /// attention" instead — and KDE, Sway and others do. Unlike <see cref="Activate"/> the
    /// token carries no serial: the window must not take the focus from where the user is.</summary>
    public void RequestAttention()
    {
        if (_surface is null || _platform.Activation is not { } activation) return;

        XdgActivationTokenV1 token = activation.GetActivationToken();

        token.SetAppId(App.Id);

        token.Done += value =>
        {
            if (_surface is not null) activation.Activate(value, _surface);
            token.Destroy();
        };

        token.Commit();
    }
}
