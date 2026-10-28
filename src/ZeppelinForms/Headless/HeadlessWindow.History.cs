using ZeppelinForms.Forms.Controls.Navigation;

namespace ZeppelinForms.Headless;

/// <summary>A history like a browser's, for the tests of a navigation stack that tracks it:
/// the entries are kept as a list of locations, the first one the page's own.</summary>
public sealed partial class HeadlessWindow : INavigationHistory
{
    /// <summary>The entries, the page's own first and the current one last.</summary>
    internal List<string> HistoryEntries { get; } = [string.Empty];

    string? INavigationHistory.TakeInitialRoute() => _platform.TakeInitialRoute();

    void INavigationHistory.Push(string location) => HistoryEntries.Add(location);

    void INavigationHistory.Replace(string location) => HistoryEntries[^1] = location;

    void INavigationHistory.Back(int count)
    {
        for (int i = 0; i < count && HistoryEntries.Count > 1; i++)
            HistoryEntries.RemoveAt(HistoryEntries.Count - 1);
    }
}
