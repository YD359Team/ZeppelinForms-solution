using Xunit;
using ZeppelinForms.Accessibility;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Theming;

namespace ZeppelinForms.UnitTests;

/// <summary>
/// The system's text scale, opt-in through App.UseSystemTextScale, and the
/// accessibility audit of the F12 inspector.
/// </summary>
[Collection("Platform")]
public class TextScaleAndAuditTests
{
    private sealed class FakeAppearance : ISystemAppearance
    {
        public bool IsDark => false;

        public Color? AccentColor => null;

        public float TextScale { get; set; } = 1f;

        public event EventHandler? Changed;

        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }

    private static void Restoring(Action test)
    {
        Theme themeBefore = App.RequestedTheme;
        Font fontBefore = Font.Default;

        try
        {
            test();
        }
        finally
        {
            App.UseSystemTextScale(false);
            App.UseSystemAppearance(null);
            App.Theme = themeBefore;
            Font.Default = fontBefore;
        }
    }

    private static Form CreateForm(UIElement content)
    {
        var form = new Form { Size = new Size(500, 300), Content = content };

        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return form;
    }

    private static StackPanel Panel(params UIElement[] children)
    {
        var panel = new StackPanel { Background = Colors.White };

        foreach (UIElement child in children)
            panel.Children.Add(child);

        return panel;
    }

    // ===== text scale =====

    [Fact]
    public void ScaleIsOptIn()
    {
        Restoring(() =>
        {
            App.UseSystemAppearance(new FakeAppearance { TextScale = 1.5f });

            Assert.Equal(1f, App.TextScale);

            App.UseSystemTextScale();
            Assert.Equal(1.5f, App.TextScale);
        });
    }

    [Fact]
    public void EveryFontIsScaled()
    {
        Restoring(() =>
        {
            App.Theme = Themes.Light;

            var plain = new Label { Text = "plain" };
            var title = new Label { Text = "title", TextStyle = TextStyle.Title };
            var own = new Label { Text = "own", Font = new Font("Arial", 10) };
            CreateForm(Panel(plain, title, own));

            float before = plain.EffectiveFont.Size;

            App.UseSystemAppearance(new FakeAppearance { TextScale = 1.5f });
            App.UseSystemTextScale();

            Assert.Equal(before * 1.5f, plain.EffectiveFont.Size, 3);
            Assert.Equal(TypeRamp.Default.Title.Size * 1.5f, title.EffectiveFont.Size, 3);

            // an own font too: the user asked for larger text everywhere
            Assert.Equal(15f, own.EffectiveFont.Size, 3);
        });
    }

    [Fact]
    public void ScaleChangeMeasuresAgain()
    {
        Restoring(() =>
        {
            var system = new FakeAppearance();
            var label = new Label
            {
                Text = "x",
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
            };

            Form form = CreateForm(Panel(label));
            App.UseSystemAppearance(system);
            App.UseSystemTextScale();

            float before = label.DesiredSize.Height;

            system.TextScale = 2f;
            system.RaiseChanged();
            form.UpdateLayout();

            Assert.True(label.DesiredSize.Height > before * 1.5f);
        });
    }

    [Fact]
    public void BrokenScaleIsClamped()
    {
        Restoring(() =>
        {
            var system = new FakeAppearance { TextScale = 0.5f };
            App.UseSystemAppearance(system);
            App.UseSystemTextScale();

            Assert.Equal(1f, App.TextScale);

            system.TextScale = 10f;
            system.RaiseChanged();

            Assert.Equal(3f, App.TextScale);
        });
    }

    // ===== audit =====

    private static IReadOnlyList<AccessibilityIssue> Audit(UIElement content)
    {
        Form form = CreateForm(content);
        return AccessibilityAudit.Run(form);
    }

    [Fact]
    public void IconButtonNeedsAName()
    {
        var unnamed = new Button { Size = new Size(32, 32) };
        var tipped = new Button { Size = new Size(32, 32), ToolTip = "Delete" };

        IReadOnlyList<AccessibilityIssue> issues = Audit(Panel(unnamed, tipped));

        AccessibilityIssue issue = Assert.Single(issues, i => i.Kind == AccessibilityIssueKind.MissingName);
        Assert.Same(unnamed, issue.Element);
    }

    [Fact]
    public void FieldIsNamedByItsLabel()
    {
        var labelled = new TextBox();
        var bare = new TextBox();
        var label = new Label { Text = "Name", Target = labelled };

        IReadOnlyList<AccessibilityIssue> issues = Audit(Panel(label, labelled, bare));

        AccessibilityIssue issue = Assert.Single(issues, i => i.Kind == AccessibilityIssueKind.MissingName);
        Assert.Same(bare, issue.Element);
    }

    [Fact]
    public void LowContrastTextIsFound()
    {
        var faint = new Label { Text = "faint", TextColor = new Color(0x99, 0x99, 0x99) };
        var dark = new Label { Text = "dark", TextColor = Colors.Black };

        IReadOnlyList<AccessibilityIssue> issues = Audit(Panel(faint, dark));

        AccessibilityIssue issue = Assert.Single(issues, i => i.Kind == AccessibilityIssueKind.LowTextContrast);
        Assert.Same(faint, issue.Element);
    }

    [Fact]
    public void LargeTextNeedsLess()
    {
        // about 3.03:1 on white: enough for large text, not for body text
        var gray = new Color(0x94, 0x94, 0x94);

        var large = new Label { Text = "large", TextColor = gray, Font = new Font("Arial", 24) };
        var small = new Label { Text = "small", TextColor = gray, Font = new Font("Arial", 14) };

        IReadOnlyList<AccessibilityIssue> issues = Audit(Panel(large, small));

        AccessibilityIssue issue = Assert.Single(issues, i => i.Kind == AccessibilityIssueKind.LowTextContrast);
        Assert.Same(small, issue.Element);
    }

    [Fact]
    public void DisabledTextIsExempt()
    {
        var disabled = new Label { Text = "off", TextColor = new Color(0xCC, 0xCC, 0xCC), IsEnabled = false };

        Assert.DoesNotContain(Audit(Panel(disabled)), i => i.Kind == AccessibilityIssueKind.LowTextContrast);
    }

    [Fact]
    public void SmallTargetIsFound()
    {
        var tiny = new Button
        {
            Text = "x",
            Size = new Size(16, 16),
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        AccessibilityIssue issue = Assert.Single(Audit(Panel(tiny)), i => i.Kind == AccessibilityIssueKind.SmallTarget);
        Assert.Same(tiny, issue.Element);
    }

    [Fact]
    public void DecorationIsNotAnIssue()
    {
        var picture = new PictureBox { Size = new Size(40, 40) };
        var decoration = new PictureBox { Size = new Size(40, 40), IsAccessibilityHidden = true };

        AccessibilityIssue issue = Assert.Single(Audit(Panel(picture, decoration)), i => i.Kind == AccessibilityIssueKind.UnnamedImage);
        Assert.Same(picture, issue.Element);
    }

    [Fact]
    public void InspectorShowsTheAudit()
    {
        Form form = CreateForm(Panel(new Button { Size = new Size(32, 32) }));
        int overlays = form.Overlays.Count;

        HeadlessInput.PressKey(form, Key.F12);

        Assert.NotEmpty(form.AccessibilityIssues);
        Assert.Contains(form.Overlays, overlay => overlay is ListBox);

        HeadlessInput.PressKey(form, Key.F12);

        Assert.Equal(overlays, form.Overlays.Count);
    }
}