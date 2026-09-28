using Xunit;
using ZeppelinForms.Diagnostics;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

[Collection("Platform")]
public class MeasureCacheTests
{
    /// <summary>Counts measure calls and can change its size
    /// silently — this is how the cache verification mode is checked.</summary>
    private sealed class CountingBox : UnitControl
    {
        public int Measures { get; private set; }

        /// <summary>Changes without Invalidate on purpose.</summary>
        public float ReportedHeight = 20f;

        public override void Draw(Graphics g) { }

        protected override Size MeasureOverride(Size availableSize)
        {
            Measures++;

            return new Size(100, ReportedHeight);
        }
    }

    private static (Form Form, StackPanel Panel, CountingBox Box, Label Label) CreateForm()
    {
        var platform = new HeadlessPlatform();

        var box = new CountingBox();
        var label = new Label { Text = "начальный" };

        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        panel.Children.Add(box);
        panel.Children.Add(label);

        var form = new Form { Size = new Size(400, 300), Content = panel };
        platform.CreateWindow(form);
        form.UpdateLayout();

        return (form, panel, box, label);
    }

    [Fact]
    public void RepeatedLayoutDoesNotRemeasure()
    {
        var (form, _, box, _) = CreateForm();

        int before = box.Measures;

        form.UpdateLayout();
        form.UpdateLayout();

        Assert.Equal(before, box.Measures);
    }

    [Fact]
    public void SiblingChangeDoesNotRemeasureNeighbour()
    {
        var (form, _, box, label) = CreateForm();

        int before = box.Measures;

        // the label asks for a recompute, the panel is measured again — but the
        // neighbour gets the same constraint, and its measure is taken from the cache
        label.Text = "другой текст";
        form.UpdateLayout();

        Assert.Equal(before, box.Measures);
    }

    [Fact]
    public void ChangedTextIsRemeasured()
    {
        var (form, _, _, label) = CreateForm();

        float before = label.DesiredSize.Width;

        label.Text = "текст гораздо длиннее прежнего, чтобы ширина заведомо выросла";
        form.UpdateLayout();

        Assert.True(
            label.DesiredSize.Width > before,
            "a text change must lead to a new measure");
    }

    [Fact]
    public void ConstraintChangeRemeasures()
    {
        var (form, panel, box, _) = CreateForm();

        int before = box.Measures;

        // the panel's padding changes the constraint its children get,
        // and for a different constraint the cache doesn't fit by definition
        panel.Padding = new Thickness(20);
        form.UpdateLayout();

        Assert.True(box.Measures > before);
    }

#if DEBUG
    [Fact]
    public void VerificationCatchesMissingInvalidate()
    {
        var (form, _, box, label) = CreateForm();

        bool verifyBefore = UIElement.VerifyMeasureCache;
        ContractViolationBehavior behaviorBefore = ZfContract.Behavior;
        string? violation = null;

        void OnViolated(object? sender, string message) => violation = message;

        UIElement.VerifyMeasureCache = true;
        ZfContract.Behavior = ContractViolationBehavior.Silent;
        ZfContract.Violated += OnViolated;

        try
        {
            // the size changes silently — exactly the mistake the mode catches
            box.ReportedHeight = 60f;

            // the recompute is asked for by a neighbour: box's constraint is the same as before
            label.Text = "ещё один текст";
            form.UpdateLayout();

            Assert.NotNull(violation);
            Assert.Contains("CountingBox", violation);
        }
        finally
        {
            ZfContract.Violated -= OnViolated;
            ZfContract.Behavior = behaviorBefore;
            UIElement.VerifyMeasureCache = verifyBefore;
        }
    }
#endif
}