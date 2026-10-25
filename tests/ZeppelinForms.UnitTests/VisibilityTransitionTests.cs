using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>Form.Hide and Visible: a window off the screen and back, still open all the
/// while — the ground the tray icon stands on.</summary>
[Collection("Platform")]
public sealed class FormVisibilityTests
{
    private static (Form Form, HeadlessWindow Window) Shown(Form? form = null)
    {
        form ??= new Form { Size = new Size(400, 300) };
        var window = (HeadlessWindow)new HeadlessPlatform().CreateWindow(form);
        form.Show();

        return (form, window);
    }

    [Fact]
    public void AHiddenFormKeepsItsWindowAndComesBackAsItWas()
    {
        var form = new Form { Size = new Size(400, 300) };
        var box = new TextBox();
        form.Content = new StackPanel { Children = { box } };

        int loads = 0, shows = 0;
        var changes = new List<bool>();
        form.Load += (_, _) => loads++;
        form.Shown += (_, _) => shows++;
        form.VisibleChanged += (_, _) => changes.Add(form.Visible);

        (_, HeadlessWindow window) = Shown(form);
        Assert.True(form.Visible);
        Assert.Same(box, form.FocusedElement);

        form.Hide();

        Assert.False(form.Visible);
        Assert.False(window.IsVisible);
        Assert.False(window.IsClosed);
        Assert.Contains(form, Form.OpenForms);

        form.Show();

        Assert.True(form.Visible);
        Assert.True(window.IsVisible);
        Assert.Same(box, form.FocusedElement);

        // Load is of the first showing, Shown of every one
        Assert.Equal(1, loads);
        Assert.Equal(2, shows);
        Assert.Equal([true, false, true], changes);

        form.Close();
    }

    [Fact]
    public void VisibleShowsAndHides()
    {
        (Form form, HeadlessWindow window) = Shown();

        form.Visible = false;
        Assert.False(window.IsVisible);

        form.Visible = true;
        Assert.True(window.IsVisible);

        // a second showing of a shown window changes nothing
        form.Show();
        Assert.Equal(1, window.HideCount);

        form.Close();
    }

    [Fact]
    public void AFormNotShownHasNothingToHide()
    {
        var form = new Form();

        form.Hide();

        Assert.False(form.Visible);
    }

    [Fact]
    public void ClosingTheWindowEndsItsVisibility()
    {
        (Form form, _) = Shown();
        int changes = 0;
        form.VisibleChanged += (_, _) => changes++;

        form.Hide();
        form.Close();

        // hidden already: the close changes nothing more
        Assert.False(form.Visible);
        Assert.Equal(1, changes);
        Assert.DoesNotContain(form, Form.OpenForms);
    }

    [Fact]
    public void AFormShownInANewWindowIsShownForTheFirstTimeAgain()
    {
        (Form form, _) = Shown();
        int shows = 0;
        form.Shown += (_, _) => shows++;

        form.Close();
        var window = (HeadlessWindow)new HeadlessPlatform().CreateWindow(form);
        form.Show();

        Assert.Equal(1, shows);
        Assert.True(form.Visible);
        Assert.True(window.IsVisible);

        form.Close();
    }

    [Fact]
    public void ClosingToTheTrayIsACancelledCloseAndAHide()
    {
        (Form form, HeadlessWindow window) = Shown();

        bool quitting = false;

        form.Closing += (_, e) =>
        {
            if (quitting) return;

            e.Cancel = true;
            form.Hide();
        };

        form.Close();

        Assert.False(window.IsClosed);
        Assert.False(form.Visible);
        Assert.Contains(form, Form.OpenForms);

        // the tray menu's Exit
        quitting = true;
        form.Close();
        Assert.True(window.IsClosed);
    }

    [Fact]
    public async Task ADialogCantBeHidden()
    {
        (Form main, _) = Shown();
        var dialog = new Form { Size = new Size(200, 120) };

        Task<DialogResult<object>> result = dialog.ShowDialogAsync<object>(main);

        Assert.Throws<InvalidOperationException>(dialog.Hide);
        Assert.True(dialog.Visible);

        dialog.Cancel();
        await result;

        main.Close();
    }

    [Fact]
    public void ThePlatformsSayWhetherTheyHide()
    {
        Assert.True(new HeadlessPlatform().Capabilities.HasFlag(PlatformCapabilities.WindowHiding));
    }
}