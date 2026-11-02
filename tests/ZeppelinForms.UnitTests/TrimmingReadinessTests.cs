using System.Diagnostics.CodeAnalysis;
using System.Reflection;
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

        // the editors stand in the grid's rows since 0.16, not right in the grid
        ComboBox? editor = grid.Children.OfType<StackPanel>().Single().Children
            .OfType<PropertyGridRow>().Select(row => row.Editor).OfType<ComboBox>()
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

    /// <summary>0.21: a generic parameter handed on to a type that wants members of it
    /// kept — StyledProperty&lt;T&gt;, Lazy&lt;T&gt; — asks for at least as much itself. The
    /// WebAssembly publish failed on it (IL2091): a binding's typed end held a
    /// StyledProperty&lt;TValue&gt; of a TValue without the annotation, and the font cache
    /// a Lazy&lt;TValue&gt;. A build doesn't run the trimmer; the signatures are checked
    /// here — fields, parameters and results, compiler-made types included.</summary>
    [Theory]
    [InlineData("ZeppelinForms")]
    [InlineData("ZeppelinForms.Skia")]
    public void GenericParametersCarryTheAnnotationsTheyAreHandedOnTo(string assemblyName)
    {
        Assembly assembly = Assembly.Load(assemblyName);
        var problems = new List<string>();

        foreach (Type type in assembly.GetTypes())
        {
            const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic |
                                     BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

            foreach (FieldInfo field in type.GetFields(All))
                Check(field.FieldType, $"{type}.{field.Name}", problems);

            foreach (MethodBase method in type.GetMethods(All).Cast<MethodBase>().Concat(type.GetConstructors(All)))
            {
                foreach (ParameterInfo parameter in method.GetParameters())
                    Check(parameter.ParameterType, $"{type}.{method.Name}({parameter.Name})", problems);

                if (method is MethodInfo { ReturnType: var result })
                    Check(result, $"{type}.{method.Name} result", problems);
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>Every generic parameter given as an argument of a constructed type
    /// somewhere in <paramref name="type"/> must have the annotation of the parameter it
    /// stands for.</summary>
    private static void Check(Type type, string where, List<string> problems)
    {
        if (type.HasElementType)
        {
            Check(type.GetElementType()!, where, problems);
            return;
        }

        if (!type.IsGenericType || type.IsGenericTypeDefinition) return;

        Type[] parameters = type.GetGenericTypeDefinition().GetGenericArguments();
        Type[] arguments = type.GetGenericArguments();

        for (int i = 0; i < arguments.Length; i++)
        {
            Type argument = arguments[i];

            if (argument.IsGenericParameter)
            {
                DynamicallyAccessedMemberTypes wanted = Annotation(parameters[i]);
                DynamicallyAccessedMemberTypes given = Annotation(argument);

                if ((wanted & ~given) != 0)
                    problems.Add($"{where}: {argument.Name} for {type.Name} lacks {wanted & ~given}");
            }
            else
            {
                Check(argument, where, problems);
            }
        }
    }

    private static DynamicallyAccessedMemberTypes Annotation(Type parameter) =>
        parameter.GetCustomAttribute<DynamicallyAccessedMembersAttribute>()?.MemberTypes
        ?? DynamicallyAccessedMemberTypes.None;

    [Fact]
    public void ABindingToAMissingPropertySaysWhereItLooked()
    {
        var label = new Label();

        var error = Assert.Throws<ArgumentException>(() =>
            label.Bind(Label.TextProperty, new TextBox(), "Nope"));

        Assert.Contains("TextBox", error.Message, StringComparison.Ordinal);
    }
}
