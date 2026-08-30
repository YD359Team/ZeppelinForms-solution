using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.UnitTests.Snapshots;
using ZeppelinForms.Windows;

namespace ZeppelinForms.UnitTests;

[Collection("Platform")]
public class ButtonSnapshotTests
{
    [Fact]
    public void Кнопка_в_обычном_состоянии()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Снимки привязаны к системным шрифтам");

        var button = Buttons.Primary("Сохранить");
        button.Size = new Size(140, 36);

        var form = new Form { Size = new Size(200, 80), Content = button };
        new WindowsPlatform().CreateWindow(form);

        SnapshotAssert.Matches(button, "button-primary");
    }
}
