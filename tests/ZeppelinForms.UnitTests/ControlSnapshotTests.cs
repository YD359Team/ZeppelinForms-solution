using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Headless;
using ZeppelinForms.UnitTests.Snapshots;

namespace ZeppelinForms.UnitTests;

[Collection("Platform")]
public class ControlSnapshotTests : IClassFixture<SnapshotFixture>
{
    private readonly SnapshotFixture _fixture;

    public ControlSnapshotTests(SnapshotFixture fixture) => _fixture = fixture;

    [Fact]
    public void ButtonNormalTest()
    {
        var button = Buttons.Primary("Сохранить");
        button.Size = new Size(140, 36);

        var form = new Form { Size = new Size(180, 60), Content = button };
        new HeadlessPlatform(registerServices: false).CreateWindow(form);

        SnapshotAssert.Matches(form, "button-primary");
    }

    [Fact]
    public void CheckBoxCheckedTest()
    {
        var checkBox = new CheckBox { Text = "Готово", IsChecked = true };

        var form = new Form { Size = new Size(160, 40), Content = checkBox };
        new HeadlessPlatform(registerServices: false).CreateWindow(form);

        SnapshotAssert.Matches(form, "checkbox-checked");
    }
}