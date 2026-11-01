namespace ZeppelinForms.Headless;

/// <summary>A jump list without a taskbar: what the application handed over, kept as it was
/// at that moment for the test to read.</summary>
public sealed partial class HeadlessPlatform : IJumpListPlatform
{
    private readonly List<string> _recentDocuments = [];

    /// <summary>The tasks of the last <see cref="JumpList.Apply"/>, copied as they were then.</summary>
    public IReadOnlyList<JumpTask> JumpListTasks { get; private set; } = [];

    /// <summary>The categories of the last <see cref="JumpList.Apply"/>, copied as they were then.</summary>
    public IReadOnlyList<JumpCategory> JumpListCategories { get; private set; } = [];

    /// <summary>How many times a list was handed over.</summary>
    public int JumpListApplyCount { get; private set; }

    /// <summary>The documents given to <see cref="App.AddRecentDocument"/>, oldest first.</summary>
    public IReadOnlyList<string> RecentDocuments => _recentDocuments;

    /// <summary>Refuse the lists, as a shell does that turned them off.</summary>
    public bool RefuseJumpList { get; set; }

    bool IJumpListPlatform.ApplyJumpList(JumpList list)
    {
        if (RefuseJumpList) return false;

        JumpListTasks = [.. list.Tasks];

        JumpListCategories = [.. list.Categories.Select(category =>
        {
            var copy = new JumpCategory(category.Title);
            copy.Items.AddRange(category.Items);
            return copy;
        })];

        JumpListApplyCount++;
        return true;
    }

    void IJumpListPlatform.AddRecentDocument(string path) => _recentDocuments.Add(path);
}
