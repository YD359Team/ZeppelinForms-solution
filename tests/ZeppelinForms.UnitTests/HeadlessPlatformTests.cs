using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

[Collection("Platform")]
public class HeadlessPlatformTests
{
    [Fact]
    public void ButtonClicked()
    {
        // выравнивание по умолчанию у UnitControl — Center, поэтому позицию
        // задаём явно, иначе тест зависит от размеров формы
        var button = new Button
        {
            Text = "OK",
            Size = new Size(100, 30),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        bool clicked = false;
        button.Click += (_, _) => clicked = true;

        var form = new Form { Size = new Size(400, 300), Content = button };
        new HeadlessPlatform().CreateWindow(form);

        HeadlessInput.Click(form, 50, 15);

        Assert.True(clicked);
    }

    [Fact]
    public void ClickOutsideButtonDoesNotFire()
    {
        var button = new Button
        {
            Text = "OK",
            Size = new Size(100, 30),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        bool clicked = false;
        button.Click += (_, _) => clicked = true;

        var form = new Form { Size = new Size(400, 300), Content = button };
        new HeadlessPlatform().CreateWindow(form);

        HeadlessInput.Click(form, 300, 200);

        Assert.False(clicked);
    }
}
