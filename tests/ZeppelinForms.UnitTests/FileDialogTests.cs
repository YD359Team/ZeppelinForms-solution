using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Dialogs;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.UnitTests;

/// <summary>The framework's own file dialog: the kinds its icons tell apart, the path
/// field that is a search box too, and the layout under it — a flexible field in a row
/// and the rows of a list.</summary>
[Collection("Platform")]
public sealed class FileDialogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "zf-fd-" + Guid.NewGuid().ToString("N"));

    public FileDialogTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Photos"));
        Directory.CreateDirectory(Path.Combine(_root, "Notes"));

        foreach (string name in (string[])["a.txt", "b.png", "report.pdf", "song.mp3", "movie.mkv", "pack.tar.gz", "setup.exe", "readme"])
            File.WriteAllText(Path.Combine(_root, name), name);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private (FileDialogForm Form, TextBox Path, ListBox List) Open(FileDialogMode mode = FileDialogMode.Open)
    {
        var form = new FileDialogForm(new FileDialogOptions { InitialDirectory = _root }, mode);
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return (form, Find<TextBox>(form.Content!), Find<ListBox>(form.Content!));
    }

    private static T Find<T>(UIElement root) where T : UIElement =>
        root as T ?? (root as PanelControl)?.Children.Select(Find<T>).FirstOrDefault(found => found is not null)!;

    private static string[] Names(ListBox list) => [.. list.Items.Select(item => item!.ToString()!)];

    private static void Enter(Form form, TextBox box, string text)
    {
        form.FocusElement(box);
        box.Text = text;
        form.OnKeyDown(Key.Enter, KeyModifiers.None, false);
    }

    // ===== kinds =====

    [Theory]
    [InlineData("photo.JPG", nameof(FileKind.Image))]
    [InlineData("notes.txt", nameof(FileKind.Text))]
    [InlineData("report.pdf", nameof(FileKind.Text))]
    [InlineData("backup.tar.gz", nameof(FileKind.Archive))]
    [InlineData("song.flac", nameof(FileKind.Audio))]
    [InlineData("movie.mkv", nameof(FileKind.Video))]
    [InlineData("setup.exe", nameof(FileKind.Executable))]
    [InlineData("Shortcut.lnk", nameof(FileKind.Shortcut))]
    [InlineData("app.desktop", nameof(FileKind.Shortcut))]
    [InlineData("data.dat", nameof(FileKind.Other))]
    [InlineData("Makefile", nameof(FileKind.Other))]
    [InlineData(".hidden", nameof(FileKind.Other))]
    public void TheNameTellsTheKind(string name, string kind) =>
        Assert.Equal(kind, FileKinds.FromName(name).ToString());

    [Fact]
    public void ALinkIsAShortcutAndTheExecuteBitMakesAProgram()
    {
        if (OperatingSystem.IsWindows()) return;

        string script = Path.Combine(_root, "build");
        File.WriteAllText(script, "#!/bin/sh");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        string link = Path.Combine(_root, "to-a");
        File.CreateSymbolicLink(link, Path.Combine(_root, "a.txt"));

        Assert.Equal(FileKind.Executable, FileKinds.Of(script));
        Assert.Equal(FileKind.Shortcut, FileKinds.Of(link));
        Assert.Equal(FileKind.Text, FileKinds.Of(Path.Combine(_root, "a.txt")));
    }

    [Fact]
    public void EveryKindHasAnIconOfItsOwn()
    {
        string[] icons = [.. Enum.GetValues<FileKind>().Select(kind => FileKinds.Icon(kind).PathData)];

        Assert.Equal(icons.Length, icons.Distinct().Count());

        // all on one grid, so a short glyph is not blown up to a tall one's size
        Assert.All(icons, icon => Assert.StartsWith("M0 0M24 24", icon));
    }

    // ===== rows =====

    [Fact]
    public void FoldersComeFirstWithoutBrackets()
    {
        (_, _, ListBox list) = Open();

        string[] names = Names(list);

        Assert.Equal(["Notes", "Photos"], names[..2]);
        Assert.DoesNotContain(names, name => name.StartsWith('['));
    }

    [Fact]
    public void ARowStartsAtTheListsLeftEdge()
    {
        (_, _, ListBox list) = Open();

        UIElement row = list.ContainerFromIndex(0)!;

        Assert.Equal(0f, row.Position.X, 0.01f);
        Assert.True(row.ActualSize.Width > list.ActualSize.Width / 2, "the row spans the list");
    }

    // ===== the path field =====

    [Fact]
    public void ThePathFieldShowsTheFolder()
    {
        (_, TextBox path, _) = Open();

        Assert.Equal(_root, path.Text);
        Assert.False(string.IsNullOrEmpty(path.Watermark));
    }

    [Fact]
    public void AFolderTypedInGoesThere()
    {
        (FileDialogForm form, TextBox path, ListBox list) = Open();

        Enter(form, path, "Photos");

        Assert.Equal(Path.Combine(_root, "Photos"), path.Text);
        Assert.Empty(list.Items);
    }

    [Fact]
    public void AnythingElseIsLookedForInTheFolder()
    {
        (FileDialogForm form, TextBox path, ListBox list) = Open();

        Enter(form, path, "O");
        Assert.Equal(["Notes", "Photos", "movie.mkv", "report.pdf", "song.mp3"], Names(list));

        // a pattern matches whole names
        Enter(form, path, "*.p*");
        Assert.Equal(["b.png", "report.pdf"], Names(list));

        // the query stays in the field, and an empty field shows the whole folder
        Assert.Equal("*.p*", path.Text);

        Enter(form, path, string.Empty);
        Assert.Equal(10, list.Items.Count);
        Assert.Equal(_root, path.Text);
    }

    [Fact]
    public void AFileTypedInIsPicked()
    {
        (FileDialogForm form, TextBox path, _) = Open();

        Enter(form, path, Path.Combine(_root, "song.mp3"));

        DialogResult<string[]> result = form.Result<string[]>();
        Assert.True(result.IsAccepted);
        Assert.Equal([Path.Combine(_root, "song.mp3")], result.Value!);
    }

    [Fact]
    public void TheFolderDialogDoesNotPickAFile()
    {
        (FileDialogForm form, TextBox path, ListBox list) = Open(FileDialogMode.Folder);

        Enter(form, path, "a.txt");

        Assert.False(form.Result<string[]>().IsAccepted);
        Assert.Empty(list.Items);
    }

    // ===== a flexible child of a stack =====

    [Fact]
    public void AFlexibleChildFillsItsShareAndTheNextStartsAfterIt()
    {
        var field = new TextBox { FlexGrow = 1 };
        var button = new Button { Text = "Go", Size = new Size(100, float.NaN) };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { field, button } };
        var form = new Form { Size = new Size(400, 100), Content = row };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        Assert.Equal(0f, field.Position.X, 0.01f);
        Assert.Equal(400f - 100f - 8f, field.ActualSize.Width, 0.5f);
        Assert.Equal(field.Position.X + field.ActualSize.Width + 8f, button.Position.X, 0.5f);
    }
}