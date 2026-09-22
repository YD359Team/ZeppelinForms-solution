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

[Collection("Platform")]
public class ReducedMotionTests
{
    private sealed class FakeSystem : ISystemMotionSettings
    {
        public bool PrefersReducedMotion { get; set; }

        public event EventHandler? Changed;

        public void Toggle(bool reduced)
        {
            PrefersReducedMotion = reduced;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class ProbeBox : UnitControl
    {
        public float SeenRotation { get; private set; } = float.NaN;

        public override void Draw(Graphics g) => SeenRotation = Rotation;

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

        box.Transitions.Add(Transition.Ease(UIElement.RotationProperty, 200, Easing.Linear));

        return (form, box);
    }

    private static void Render(Form form) =>
        ElementTreeRenderer.Draw(form.Content!, new HeadlessGraphics());

    /// <summary>Настройка статическая — каждый тест возвращает её как было.</summary>
    private static void WithPreference(MotionPreference preference, Action body)
    {
        MotionPreference before = Motion.Preference;

        try
        {
            Motion.Preference = preference;
            body();
        }
        finally
        {
            Motion.Preference = before;
        }
    }

    [Fact]
    public void ReducedPreferenceMakesTransitionsInstant()
    {
        WithPreference(MotionPreference.Reduced, () =>
        {
            var (form, box) = CreateForm();

            box.Rotation = 90f;
            Render(form);

            // уменьшенное движение: сразу конечное значение, без полпути
            Assert.Equal(90f, box.SeenRotation);
        });
    }

    [Fact]
    public void FullPreferenceKeepsTransitions()
    {
        WithPreference(MotionPreference.Full, () =>
        {
            var (form, box) = CreateForm();

            box.Rotation = 90f;

            form.Clock.Advance(TimeSpan.FromMilliseconds(100));
            Render(form);

            Assert.InRange(box.SeenRotation, 30f, 60f);
        });
    }

    [Fact]
    public void SystemSettingIsFollowedAndReported()
    {
        WithPreference(MotionPreference.System, () =>
        {
            var system = new FakeSystem();
            int changes = 0;

            void OnChanged(object? sender, EventArgs e) => changes++;

            Motion.UseSystemSettings(system);
            Motion.Changed += OnChanged;

            try
            {
                Assert.False(Motion.IsReduced);

                system.Toggle(true);

                Assert.True(Motion.IsReduced);
                Assert.Equal(1, changes);
            }
            finally
            {
                Motion.Changed -= OnChanged;
                system.Toggle(false);
            }
        });
    }

    [Fact]
    public void AppPreferenceOverridesSystem()
    {
        var system = new FakeSystem { PrefersReducedMotion = true };

        Motion.UseSystemSettings(system);

        try
        {
            WithPreference(MotionPreference.Full, () => Assert.False(Motion.IsReduced));
            WithPreference(MotionPreference.System, () => Assert.True(Motion.IsReduced));
        }
        finally
        {
            system.PrefersReducedMotion = false;
        }
    }
}