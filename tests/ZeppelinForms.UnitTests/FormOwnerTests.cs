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
}