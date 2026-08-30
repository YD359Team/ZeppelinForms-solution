using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

public class HeadlessPlatformTests
{
    [Fact]
    public void ButtonClicked()
    {
        var button = new Button { Text = "OK", Size = new Size(100, 30) };
        bool clicked = false;
        button.Click += (_, _) => clicked = true;

        var form = new Form { Size = new Size(400, 300), Content = button };
        new HeadlessPlatform().CreateWindow(form);

        HeadlessInput.Click(form, 50, 15);

        Assert.True(clicked);
    }
}
