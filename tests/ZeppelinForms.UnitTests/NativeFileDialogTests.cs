using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Dialogs;
using ZeppelinForms.Headless;
using ZeppelinForms.Linux.DBus;
using ZeppelinForms.Linux.Tray;

namespace ZeppelinForms.UnitTests;

/// <summary>0.19: the system's file dialogs before the framework's own, and the picked
/// files — a stream to read or to write, wherever the file lives.</summary>
[Collection("Platform")]
public sealed class NativeFileDialogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "zf-nfd-" + Guid.NewGuid().ToString("N"));

    private readonly List<(FileDialogMode Mode, FileDialogOptions Options)> _asked = [];

    public NativeFileDialogTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    /// <summary>A form on a platform whose system dialog answers <paramref name="answer"/>.</summary>
    private Form Owner(Func<FileDialogMode, FileDialogOptions, string[]>? answer)
    {
        var platform = new HeadlessPlatform();

        platform.NativeFileDialog = answer is null ? null : (mode, options) =>
        {
            _asked.Add((mode, options));
            return answer(mode, options);
        };

        var owner = new Form { Size = new Size(400, 300) };
        platform.CreateWindow(owner);

        return owner;
    }

    private string File(string name, string content = "")
    {
        string path = Path.Combine(_root, name);
        System.IO.File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void TheSystemDialogAnswersTheSynchronousCalls()
    {
        string a = File("a.txt"), b = File("b.txt");
        Form owner = Owner((mode, _) => mode == FileDialogMode.Save ? [Path.Combine(_root, "new.txt")] : [a, b]);

        Assert.Equal(a, FileDialog.OpenFile(owner));
        Assert.Equal([a, b], FileDialog.OpenFiles(owner));
        Assert.Equal(Path.Combine(_root, "new.txt"), FileDialog.SaveFile(owner, new FileDialogOptions { FileName = "new.txt" }));

        Assert.Equal(
            [(FileDialogMode.Open, false), (FileDialogMode.Open, true), (FileDialogMode.Save, false)],
            _asked.Select(asked => (asked.Mode, asked.Options.AllowMultiple)));

        Assert.Equal("new.txt", _asked[2].Options.FileName);
    }

    [Fact]
    public async Task TheSystemDialogAnswersTheAsynchronousCalls()
    {
        Form owner = Owner((mode, _) => [_root]);

        Assert.Equal(_root, await FileDialog.SelectFolderAsync(owner));
        Assert.Equal(FileDialogMode.Folder, _asked.Single().Mode);
    }

    [Fact]
    public async Task ACancelIsNoFileAndNoSecondDialog()
    {
        Form owner = Owner((_, _) => []);

        Assert.Null(FileDialog.OpenFile(owner));
        Assert.Null(await FileDialog.SaveFileAsync(owner));
        Assert.Null(await FileDialog.PickFileAsync(owner));

        // each answered by the system's dialog alone: the framework's didn't follow
        Assert.Equal(3, _asked.Count);
        Assert.Empty(owner.OwnedForms);
    }

    [Fact]
    public void WithoutASystemDialogTheFrameworksOwnIsShown()
    {
        Form owner = Owner(answer: null);

        Task<string?> open = FileDialog.OpenFileAsync(owner);

        Assert.False(open.IsCompleted);
        Assert.IsType<FileDialogForm>(Assert.Single(owner.OwnedForms));
    }

    [Fact]
    public void UseNativeFalseAsksForTheFrameworksOwn()
    {
        Form owner = Owner((_, _) => [File("never.txt")]);

        Task<string?> open = FileDialog.OpenFileAsync(owner, new FileDialogOptions { UseNative = false });

        Assert.False(open.IsCompleted);
        Assert.Empty(_asked);
        Assert.IsType<FileDialogForm>(Assert.Single(owner.OwnedForms));
    }

    [Fact]
    public void TheCallersOptionsAreNotChanged()
    {
        Form owner = Owner((_, _) => []);
        var options = new FileDialogOptions { Title = "Import", UseNative = true };

        FileDialog.OpenFiles(owner, options);

        Assert.False(options.AllowMultiple);
        Assert.Equal("Import", _asked.Single().Options.Title);
        Assert.NotSame(options, _asked.Single().Options);
    }

    [Fact]
    public async Task APickedFileReadsAndWrites()
    {
        string path = File("orders.csv", "a,b");
        Form owner = Owner((_, _) => [path]);

        PickedFile? picked = await FileDialog.PickFileAsync(owner);

        Assert.NotNull(picked);
        Assert.Equal("orders.csv", picked.Name);
        Assert.Equal(path, picked.Path);

        await using (Stream read = await picked.OpenReadAsync(TestContext.Current.CancellationToken))
            Assert.Equal("a,b", await new StreamReader(read).ReadToEndAsync(TestContext.Current.CancellationToken));

        await using (Stream write = await picked.OpenWriteAsync(TestContext.Current.CancellationToken))
            await write.WriteAsync("1"u8.ToArray(), TestContext.Current.CancellationToken);

        // the new content only: what was there goes
        Assert.Equal("1", await System.IO.File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task APickedFileToSaveNeedNotExistYet()
    {
        string path = Path.Combine(_root, "report.txt");
        Form owner = Owner((_, _) => [path]);

        PickedFile? picked = await FileDialog.PickSaveFileAsync(owner, new FileDialogOptions { FileName = "report.txt" });

        Assert.NotNull(picked);

        await using (Stream write = await picked.OpenWriteAsync(TestContext.Current.CancellationToken))
            await write.WriteAsync("done"u8.ToArray(), TestContext.Current.CancellationToken);

        Assert.Equal("done", await System.IO.File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal(FileDialogMode.Save, _asked.Single().Mode);
    }

    [Fact]
    public async Task ManyPickedFilesKeepTheirOrder()
    {
        string a = File("1.txt"), b = File("2.txt");
        Form owner = Owner((_, _) => [b, a]);

        IReadOnlyList<PickedFile> picked = await FileDialog.PickFilesAsync(owner);

        Assert.Equal(["2.txt", "1.txt"], picked.Select(file => file.Name));
        Assert.True(_asked.Single().Options.AllowMultiple);
    }

    [Fact]
    public void APathGivenAnotherWayIsAPickedFileToo()
    {
        PickedFile file = PickedFile.FromPath(File("recent.md"));

        Assert.Equal("recent.md", file.Name);
        Assert.Equal(Path.Combine(_root, "recent.md"), file.Path);
        Assert.Equal(file.Path, file.ToString());
    }

    // ===== the portal's answers, on Linux =====

    [Theory]
    [InlineData(":1.42", "zf3", "/org/freedesktop/portal/desktop/request/1_42/zf3")]
    [InlineData(":1.7", "zf1", "/org/freedesktop/portal/desktop/request/1_7/zf1")]
    public void ThePortalsRequestPathIsKnownBeforeTheCall(string sender, string token, string path) =>
        Assert.Equal(path, LinuxFileChooser.RequestPath(sender, token));

    [Fact]
    public void ThePortalsUrisAreLocalPaths()
    {
        List<object?> results =
        [
            new object?[] { "current_filter", new DBusVariant("(sa(us))", new object?[] { "CSV", new List<object?>() }) },
            new object?[]
            {
                "uris",
                new DBusVariant("as", new List<object?> { "file:///home/me/a%20b.csv", "file:///home/me/%C3%A9t%C3%A9.txt", "mtp://phone/c.png" }),
            },
        ];

        Assert.Equal(["/home/me/a b.csv", "/home/me/été.txt"], LinuxFileChooser.Paths(results));
    }

    [Fact]
    public void AnswerWithoutUrisIsNoFile() =>
        Assert.Empty(LinuxFileChooser.Paths([new object?[] { "choices", new DBusVariant("a(ss)", new List<object?>()) }]));
}
