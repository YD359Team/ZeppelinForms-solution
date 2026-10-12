using Xunit;
using ZeppelinForms.Core.Text;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>What the framework does without enumerating assemblies or reflecting over
/// types nobody named: the generated registration of its controls, style values
/// written by the types the properties declare, the enum editor of the property grid.</summary>
[Collection("Platform")]
public class TrimmingReadinessTests
{
    private const string ScanSwitch = "ZeppelinForms.Styling.ScanAssemblies";

    [Fact]
    public void TheFrameworkListsItsControlsItself()
    {
        Type? registration = typeof(UIElement).Assembly.GetType("ZeppelinForms.Generated.StyleTypeRegistration");

        Assert.NotNull(registration);
    }

    [Fact]
    public void ASheetFindsTheControlsWithoutScanning()
    {
        AppContext.SetSwitch(ScanSwitch, false);

        try
        {
            StyleSheet sheet = StyleSheet.Parse("""
                Button { Padding: 4 8; }
                ToggleSwitch:checked { TextColor: @Accent; }
                """, "registered.zss");

            Assert.Empty(sheet.Diagnostics);
        }
        finally
        {
            AppContext.SetSwitch(ScanSwitch, true);
        }
    }

    [Fact]
    public void AnUnknownControlIsStillReported()
    {
        StyleSheet sheet = StyleSheet.Parse("Buton { Padding: 4; }", "typo.zss");

        Assert.Contains(sheet.Diagnostics, d => d.Message.Contains("Buton", StringComparison.Ordinal));
    }

    [Fact]
    public void ValuesAreWrittenByTheTypesThePropertiesDeclare()
    {
        StyleSheet sheet = StyleSheet.Parse("""
            Border.card {
                Padding: 4 8;
                CornerRadius: 6;
                BoxShadow: medium;
                BorderColor: alpha(@Accent, 0.5);
            }
            Label.rtl { FlowDirection: right-to-left; }
            Label.none { FlowDirection: none; }
            Button { CornerRadius: @Metrics.ControlCornerRadius; }
            """, "values.zss");

        Assert.Empty(sheet.Diagnostics);
    }

    [Fact]
    public void AStyledValueInsideANullableIsWrittenToo()
    {
        var label = new Label { Text = "Hello" };
        label.Classes.Add("rtl");

        var form = new Form { Size = new Size(200, 100), Content = label };
        new HeadlessPlatform().CreateWindow(form);
        form.Styles.Add(StyleSheet.Parse("Label.rtl { FlowDirection: right-to-left; }", "rtl.zss"));
        form.UpdateLayout();

        Assert.Equal(FlowDirection.RightToLeft, label.FlowDirection);
    }

    [Fact]
    public void ThePropertyGridOffersAnEnumsMembers()
    {
        var grid = new PropertyGrid { SelectedObject = new Label { Text = "x" } };

        ComboBox? editor = grid.Children.OfType<ComboBox>()
            .FirstOrDefault(c => c.Items.Count > 0 && c.Items[0] is TextWrapping);

        Assert.NotNull(editor);
        Assert.Equal([TextWrapping.NoWrap, TextWrapping.Wrap], editor!.Items.Cast<TextWrapping>());
    }

    [Fact]
    public void BindingFindsThePropertyOnTheSourcesType()
    {
        var source = new TextBox { Text = "typed" };
        var label = new Label();

        label.Bind(Label.TextProperty, source, nameof(TextBox.Text));
        Assert.Equal("typed", label.Text);

        source.Text = "changed";
        Assert.Equal("changed", label.Text);
    }

    [Fact]
    public void ABindingToAMissingPropertySaysWhereItLooked()
    {
        var label = new Label();

        var error = Assert.Throws<ArgumentException>(() =>
            label.Bind(Label.TextProperty, new TextBox(), "Nope"));

        Assert.Contains("TextBox", error.Message, StringComparison.Ordinal);
    }
}
