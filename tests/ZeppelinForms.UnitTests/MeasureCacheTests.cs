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
    /// <summary>Считает обращения к измерению и умеет менять свой размер
    /// молча — так проверяется режим сверки кэша.</summary>
    private sealed class CountingBox : UnitControl
    {
        public int Measures { get; private set; }

        /// <summary>Меняется без Invalidate намеренно.</summary>
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

        // подпись просит пересчёт, панель меряется заново — но соседу
        // приходит то же ограничение, и его измерение берётся из кэша
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
            "смена текста обязана привести к новому измерению");
    }

    [Fact]
    public void ConstraintChangeRemeasures()
    {
        var (form, panel, box, _) = CreateForm();

        int before = box.Measures;

        // отступ панели меняет ограничение, которое достаётся детям,
        // а по другому ограничению кэш не подходит по определению
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
            // меняем размер молча — именно та ошибка, которую ловит режим
            box.ReportedHeight = 60f;

            // пересчёт просит сосед: ограничение у box то же, что было
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