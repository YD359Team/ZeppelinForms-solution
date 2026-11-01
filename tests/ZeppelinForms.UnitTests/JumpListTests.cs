using Xunit;
using ZeppelinForms.Forms;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>0.20: the application's jump list — tasks, categories, the recent documents —
/// handed to the platform as a whole.</summary>
[Collection("Platform")]
public sealed class JumpListTests
{
    private static HeadlessPlatform Platform()
    {
        var platform = new HeadlessPlatform();
        _ = new App(platform) { MainForm = new Form() };

        App.JumpList.Tasks.Clear();
        App.JumpList.Categories.Clear();

        return platform;
    }

    [Fact]
    public void TheListGoesToThePlatformAsItIsWhenApplied()
    {
        HeadlessPlatform platform = Platform();

        var newDocument = new JumpTask("New document", "--new") { Description = "Start empty" };
        App.JumpList.Tasks.Add(newDocument);
        App.JumpList.Tasks.Add(JumpTask.Separator());
        App.JumpList.Tasks.Add(new JumpTask("Settings", "--settings"));

        var pinned = new JumpCategory("Pinned");
        pinned.Items.Add(new JumpTask("Orders", "orders.csv"));
        App.JumpList.Categories.Add(pinned);

        // nothing goes before Apply
        Assert.Equal(0, platform.JumpListApplyCount);

        Assert.True(App.JumpList.Apply());

        Assert.Equal(["New document", "", "Settings"], platform.JumpListTasks.Select(t => t.Title));
        Assert.True(platform.JumpListTasks[1].IsSeparator);
        Assert.Equal("--new", platform.JumpListTasks[0].Arguments);
        Assert.Equal("Orders", Assert.Single(Assert.Single(platform.JumpListCategories).Items).Title);

        // what the platform got is the list of that moment
        App.JumpList.Tasks.Clear();
        Assert.Equal(3, platform.JumpListTasks.Count);

        Assert.True(App.JumpList.Clear());
        Assert.Empty(platform.JumpListTasks);
        Assert.Empty(platform.JumpListCategories);
        Assert.Equal(2, platform.JumpListApplyCount);
    }

    [Fact]
    public void ARefusedListSaysSo()
    {
        HeadlessPlatform platform = Platform();
        platform.RefuseJumpList = true;

        App.JumpList.Tasks.Add(new JumpTask("New", "--new"));

        Assert.False(App.JumpList.Apply());
        Assert.Empty(platform.JumpListTasks);
    }

    [Fact]
    public void ARecentDocumentIsGivenByItsFullPath()
    {
        HeadlessPlatform platform = Platform();

        App.AddRecentDocument("orders.csv");

        Assert.Equal(Path.GetFullPath("orders.csv"), Assert.Single(platform.RecentDocuments));
        Assert.True(platform.Capabilities.HasFlag(PlatformCapabilities.JumpList));
    }
}
