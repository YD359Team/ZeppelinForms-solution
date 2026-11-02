using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

[Collection("Platform")]
public class FormOwnerTests
{
    private static Form CreateMain()
    {
        var platform = new HeadlessPlatform();
        var main = new Form { Size = new Size(400, 300) };

        platform.CreateWindow(main);
        main.Show();

        return main;
    }

    [Fact]
    public void ShowWithOwnerCreatesTheWindowAndLinksBoth()
    {
        Form main = CreateMain();
        var palette = new Form { Size = new Size(200, 300) };

        palette.Show(main);

        Assert.NotNull(palette.PlatformWindow);
        Assert.Same(main, palette.Owner);
        Assert.Equal([palette], main.OwnedForms);
    }

    [Fact]
    public void ClosingTheOwnerClosesWhatItOwns()
    {
        Form main = CreateMain();
        var palette = new Form { Size = new Size(200, 300) };
        bool paletteClosed = false;

        palette.Closed += (_, _) => paletteClosed = true;
        palette.Show(main);

        main.Close();

        Assert.True(paletteClosed);
        Assert.Empty(main.OwnedForms);
    }

    [Fact]
    public void ClosedFormLeavesItsOwner()
    {
        Form main = CreateMain();
        var palette = new Form { Size = new Size(200, 300) };

        palette.Show(main);
        palette.Close();

        Assert.Null(palette.Owner);
        Assert.Empty(main.OwnedForms);
    }

    [Fact]
    public void FormCanOwnNeitherItselfNorItsOwner()
    {
        Form main = CreateMain();
        var palette = new Form { Size = new Size(200, 300) };

        palette.Show(main);

        Assert.Throws<ArgumentException>(() => main.Show(main));
        Assert.Throws<ArgumentException>(() => main.Show(palette));
    }

    [Fact]
    public async Task DialogBelongsToItsOwnerWhileOpen()
    {
        Form main = CreateMain();
        var dialog = new Form { Size = new Size(200, 120) };

        Task<DialogResult<object>> shown = dialog.ShowDialogAsync<object>(main);

        Assert.Same(main, dialog.Owner);

        dialog.Cancel();
        await shown;

        Assert.Null(dialog.Owner);
    }

    [Fact]
    public async Task ADialogEnablesItsOwnerBeforeItsWindowGoes()
    {
        Form main = CreateMain();
        var ownerWindow = (HeadlessWindow)main.PlatformWindow!;
        var dialog = new Form { Size = new Size(200, 120) };

        bool? enabledAtClose = null;
        dialog.Closed += (_, _) => enabledAtClose = ownerWindow.IsEnabled;

        Task<DialogResult<object>> shown = dialog.ShowDialogAsync<object>(main);
        Assert.False(ownerWindow.IsEnabled);

        // Windows gives the activation to the next window that can take it as a window
        // is destroyed: a disabled owner can't, and another application's window came up
        dialog.Accept();
        await shown;

        Assert.True(enabledAtClose);
    }

    [Fact]
    public async Task ADialogKeptOpenKeepsItsOwnerDisabled()
    {
        Form main = CreateMain();
        var ownerWindow = (HeadlessWindow)main.PlatformWindow!;
        var dialog = new Form { Size = new Size(200, 120) };

        dialog.Closing += (_, e) => e.Cancel = true;

        _ = dialog.ShowDialogAsync<object>(main);
        dialog.Accept();

        Assert.False(ownerWindow.IsEnabled);
    }
}