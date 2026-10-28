using ZeppelinForms.Forms;

namespace ZeppelinForms.Browser;

/// <summary>What a page has of a taskbar button: the badge of an installed web application,
/// and the tab's title blinking for attention. No progress: a page has none to show.</summary>
internal sealed partial class BrowserWindow : ITaskbarWindow
{
    public void SetTaskbarProgress(TaskbarProgressState state, double value)
    {
    }

    public void SetTaskbarBadge(int? count) => Interop.SetAppBadge(count ?? 0);

    public void RequestAttention() => Interop.RequestAttention();
}
