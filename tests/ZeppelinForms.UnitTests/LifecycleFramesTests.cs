using Xunit;
using ZeppelinForms.Animation;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>То, что дёргает жизненный цикл платформы: уход в фон должен
/// останавливать выдачу кадров, возврат — возобновлять. Сами колбэки
/// Android проверить в headless нечем, а вот механизм под ними — можно.</summary>
[Collection("Platform")]
public class LifecycleFramesTests
{
    private sealed class ProbeBox : UnitControl
    {
        public override void Draw(Graphics g) { }

        protected override Size MeasureOverride(Size availableSize) => new(100, 40);
    }

    private static (Form Form, ProbeBox Box) CreateForm()
    {
        var platform = new HeadlessPlatform();

        var box = new ProbeBox();

        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        panel.Children.Add(box);

        var form = new Form { Size = new Size(400, 300), Content = panel };
        platform.CreateWindow(form);
        form.UpdateLayout();

        box.Transitions.Add(Transition.Ease(UIElement.RotationProperty, 5_000, Easing.Linear));

        return (form, box);
    }

    [Fact]
    public void RunningTransitionAsksForFrames()
    {
        var (form, box) = CreateForm();

        box.Rotation = 90f;

        Assert.True(form.PlatformWindow!.Frames.IsRunning);
    }

    [Fact]
    public void SuspendStopsFramesAndResumeBringsThemBack()
    {
        var (form, box) = CreateForm();

        box.Rotation = 90f;

        form.SuspendFrames();
        Assert.False(form.PlatformWindow!.Frames.IsRunning);

        form.ResumeFrames();
        Assert.True(form.PlatformWindow!.Frames.IsRunning);
    }

    [Fact]
    public void FinishedTransitionReleasesFrames()
    {
        var (form, box) = CreateForm();

        box.Rotation = 90f;

        form.Clock.Advance(TimeSpan.FromSeconds(6));

        // двигать больше нечего — кадры не нужны
        Assert.False(form.PlatformWindow!.Frames.IsRunning);
    }
}