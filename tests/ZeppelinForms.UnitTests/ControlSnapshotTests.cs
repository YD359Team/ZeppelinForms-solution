using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;
using ZeppelinForms.Skia;
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

        // the baseline was taken on the white the window used to be cleared with
        var form = new Form { Size = new Size(180, 60), Content = button, Background = Colors.White };
        new HeadlessPlatform(registerServices: false).CreateWindow(form);

        SnapshotAssert.Matches(form, "button-primary");
    }

    [Fact]
    public void AGradientThatMovesKeepsItsShader()
    {
        var box = new Border
        {
            Size = new Size(80, 30),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(10),
        };

        box.SetBackgroundGradient(Colors.White, Colors.Black);

        var form = new Form { Size = new Size(200, 100), Content = box, Background = Colors.White };
        new HeadlessPlatform(registerServices: false).CreateWindow(form);
        form.UpdateLayout();

        var renderer = new SkiaOffscreenRenderer();
        renderer.RenderForm(form, 200, 100, 1f);

        int made = SkiaDiagnostics.GradientShadersCreated;

        // 0.19.1: a shader, a brush and two arrays used to be made for every draw. A
        // gradient that only moves keeps its shader now
        box.Margin = new Thickness(40, 30, 0, 0);
        form.UpdateLayout();
        renderer.RenderForm(form, 200, 100, 1f);
        renderer.RenderForm(form, 200, 100, 1f);

        Assert.Equal(made, SkiaDiagnostics.GradientShadersCreated);

        // other stops are another shader
        box.SetBackgroundGradient(Colors.Black, Colors.White);
        renderer.RenderForm(form, 200, 100, 1f);

        Assert.Equal(made + 1, SkiaDiagnostics.GradientShadersCreated);
    }

    [Fact]
    public void CheckBoxCheckedTest()
    {
        var checkBox = new CheckBox { Text = "Готово", IsChecked = true };

        var form = new Form { Size = new Size(160, 40), Content = checkBox, Background = Colors.White };
        new HeadlessPlatform(registerServices: false).CreateWindow(form);

        SnapshotAssert.Matches(form, "checkbox-checked");
    }
}