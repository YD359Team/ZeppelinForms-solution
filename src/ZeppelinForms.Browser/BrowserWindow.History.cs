using ZeppelinForms.Forms.Controls.Navigation;

namespace ZeppelinForms.Browser;

/// <summary>The page's history for a navigation stack in the form: its pushes become
/// entries, the browser's back button goes back in it.</summary>
internal sealed partial class BrowserWindow : INavigationHistory
{
    public string? TakeInitialRoute() => _platform.TakeInitialRoute();

    public void Push(string location) => Interop.HistoryPush(location);

    public void Replace(string location) => Interop.HistoryReplace(location);

    public void Back(int count)
    {
        if (count > 0) Interop.HistoryBack(count);
    }
}
