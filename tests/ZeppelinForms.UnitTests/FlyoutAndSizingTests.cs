using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Dialogs;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

[Collection("Platform")]
public class FlyoutAndSizingTests
{
    [Fact]
    public void FlyoutPresenterGetsTheThemesSurface()
    {
        var platform = new HeadlessPlatform();
        var anchor = new Button { Text = "якорь" };
        var form = new Form { Size = new Size(400, 300), Content = anchor };

        platform.CreateWindow(form);
        form.UpdateLayout();

        var presenter = new FlyoutPresenter(new Label { Text = "всплывающее" });
        form.ShowFlyout(anchor, presenter);

        Assert.Equal(App.Theme.Colors.Surface, presenter.Background);
        Assert.Equal(App.Theme.Metrics.OverlayCornerRadius, presenter.CornerRadius);
    }

    [Fact]
    public void WindowTakesTheSizeOfItsContent()
    {
        _ = new HeadlessPlatform();

        var form = new Form
        {
            SizeToContent = SizeToContent.WidthAndHeight,
            Content = new Border { Size = new Size(240, 90) },
        };

        form.ApplySizeToContent();

        Assert.Equal(240, form.Size.Width);
        Assert.Equal(90, form.Size.Height);
    }

    [Fact]
    public void HeightAloneKeepsTheGivenWidth()
    {
        _ = new HeadlessPlatform();

        var form = new Form
        {
            Size = new Size(380, 999),
            SizeToContent = SizeToContent.Height,
            Content = new Border { Size = new Size(100, 60) },
        };

        form.ApplySizeToContent();

        Assert.Equal(380, form.Size.Width);
        Assert.Equal(60, form.Size.Height);
    }

    [Fact]
    public void MessageBoxIsWideEnoughForItsButtons()
    {
        _ = new HeadlessPlatform();

        var box = new MessageBoxForm(
            "Save the changes before closing?", "Showcase",
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

        box.ApplySizeToContent();

        // three buttons of 96, two gaps of 8, the padding of 16 on both sides
        Assert.True(box.Size.Width >= (3 * 96) + (2 * 8) + (2 * 16), $"width {box.Size.Width}");
    }
}