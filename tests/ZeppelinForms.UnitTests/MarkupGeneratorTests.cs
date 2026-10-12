using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;
using ZeppelinForms.Analyzers.Markup;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.UnitTests;

/// <summary>.zfml markup compiled into C#: what it builds, and the errors it reports at
/// their lines. Each case is compiled, loaded and run.</summary>
[Collection("Platform")]
public class MarkupGeneratorTests
{
    private const string Xmlns =
        "xmlns=\"https://zeppelinforms.dev/markup\" xmlns:x=\"https://zeppelinforms.dev/markup/x\"";

    private sealed class MarkupFile(string path, string text) : AdditionalText
    {
        public override string Path => path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
    }

    private sealed record Result(Assembly? Assembly, ImmutableArray<Diagnostic> Errors, string? Source)
    {
        public object Create(string type) => Activator.CreateInstance(Assembly!.GetType(type, throwOnError: true)!)!;
    }

    private static int s_build;

    private static Result Build(string markup, string code = "")
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(UIElement).Assembly.Location));

        var parse = new CSharpParseOptions(LanguageVersion.Latest);

        CSharpCompilation compilation = CSharpCompilation.Create(
            "Markup" + Interlocked.Increment(ref s_build),
            [CSharpSyntaxTree.ParseText(code, parse)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new MarkupGenerator().AsSourceGenerator()],
            [new MarkupFile("/app/Main.zfml", markup)],
            parse);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> generated);

        string? source = driver.GetRunResult().GeneratedTrees.FirstOrDefault()?.ToString();

        ImmutableArray<Diagnostic> errors = [.. generated.Concat(output.GetDiagnostics()).Where(d => d.Severity == DiagnosticSeverity.Error)];

        if (!errors.IsEmpty) return new Result(null, errors, source);

        using var stream = new MemoryStream();
        var emitted = output.Emit(stream);

        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));

        return new Result(Assembly.Load(stream.ToArray()), errors, source);
    }

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(owner)!;

    [Fact]
    public void AFormIsBuiltFromItsMarkup()
    {
        Result result = Build($"""
            <Form x:Class="App.MainForm" Title="Orders" Size="800,600" {Xmlns}>
              <StackPanel Orientation="Horizontal" Spacing="8" Padding="14 6" Background="#ff0000">
                <TextBox x:Name="Search" Watermark="Search" Size="240,NaN" IsReadOnly="true"/>
                <Button Text="Find" Classes="primary large" CornerRadius="4"/>
              </StackPanel>
            </Form>
            """);

        Assert.Empty(result.Errors);

        var form = (Form)result.Create("App.MainForm");
        Assert.Equal("Orders", form.Title);
        Assert.Equal(new Size(800, 600), form.Size);

        var panel = Assert.IsType<StackPanel>(form.Content);
        Assert.Equal(Orientation.Horizontal, panel.Orientation);
        Assert.Equal(8, panel.Spacing);
        Assert.Equal(new Thickness(14, 6), panel.Padding);
        Assert.Equal(new Color(255, 255, 0, 0), panel.Background);
        Assert.Equal(2, panel.Children.Count);

        var search = Field<TextBox>(form, "Search");
        Assert.Same(panel.Children[0], search);
        Assert.Equal("Search", search.Name);
        Assert.True(search.IsReadOnly);
        Assert.Equal(240, search.Size.Width);
        Assert.True(float.IsNaN(search.Size.Height));

        var button = Assert.IsType<Button>(panel.Children[1]);
        Assert.Contains("primary", button.Classes);
        Assert.Contains("large", button.Classes);
        Assert.Equal(new CornerRadius(4), button.CornerRadius);
    }

    [Fact]
    public void AnEventGoesToAMethodOfTheClass()
    {
        Result result = Build($"""
            <Form x:Class="App.MainForm" {Xmlns}>
              <Button x:Name="Find" Text="Find" Click="OnFind"/>
            </Form>
            """, """
            namespace App;
            public partial class MainForm
            {
                public int Clicks;
                private void OnFind(object? sender, ZeppelinForms.Input.Mouse.MouseClickEventArgs e) => Clicks++;
            }
            """);

        Assert.Empty(result.Errors);
        Assert.Contains("e1.Click += this.OnFind;", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void AHandlerOfTheWrongShapeIsReportedInTheMarkup()
    {
        Result result = Build($"""
            <Form x:Class="App.MainForm" {Xmlns}>
              <Button Click="OnFind"/>
            </Form>
            """, """
            namespace App;
            public partial class MainForm { private void OnFind() { } }
            """);

        // the compiler's own error, at the line of the markup that wrote the handler
        Diagnostic error = Assert.Single(result.Errors);
        Assert.Equal("CS0123", error.Id);
        Assert.Equal(2, error.Location.GetMappedLineSpan().StartLinePosition.Line + 1);
        Assert.EndsWith("Main.zfml", error.Location.GetMappedLineSpan().Path);
    }

    [Fact]
    public void AnElementBindsToANamedOne()
    {
        Result result = Build($$"""
            <StackPanel x:Class="App.Echo" {{Xmlns}}>
              <Label x:Name="Shown" Text="{Bind Typed.Text}"/>
              <TextBox x:Name="Typed"/>
            </StackPanel>
            """);

        Assert.Empty(result.Errors);

        object echo = result.Create("App.Echo");
        Field<TextBox>(echo, "Typed").Text = "hello";

        Assert.Equal("hello", Field<Label>(echo, "Shown").Text);
    }

    [Fact]
    public void ChildrenGoWhereTheirTypeFits()
    {
        Result result = Build($"""
            <SplitView x:Class="App.Shell" {Xmlns}>
              <SplitViewItem Text="Home"/>
              <SplitViewItem Text="Settings"/>
            </SplitView>
            """);

        Assert.Empty(result.Errors);

        var shell = (SplitView)result.Create("App.Shell");
        Assert.Equal(["Home", "Settings"], shell.Items.Select(i => i.Text));
    }

    [Fact]
    public void ApplicationControlsComeByTheirNamespace()
    {
        Result result = Build($"""
            <StackPanel x:Class="App.Board" xmlns:my="using:App.Controls" {Xmlns}>
              <my:Gauge Level="0.5"/>
            </StackPanel>
            """, """
            namespace App.Controls;
            public class Gauge : ZeppelinForms.Forms.Controls.Border { public double Level { get; set; } }
            """);

        Assert.Empty(result.Errors);

        var board = (StackPanel)result.Create("App.Board");
        Assert.Equal(0.5, (double)board.Children[0].GetType().GetProperty("Level")!.GetValue(board.Children[0])!);
    }

    [Fact]
    public void TextItemsAndPropertyElements()
    {
        Result result = Build($"""
            <StackPanel x:Class="App.Picker" {Xmlns}>
              <ComboBox x:Name="Choice">
                <x:String>One</x:String>
                <x:String>Two</x:String>
              </ComboBox>
              <Border>
                <Border.Child>
                  <Label Text="Inside"/>
                </Border.Child>
              </Border>
            </StackPanel>
            """);

        Assert.Empty(result.Errors);

        object picker = result.Create("App.Picker");
        Assert.Equal(["One", "Two"], Field<ComboBox>(picker, "Choice").Items.Cast<string>());
    }

    [Fact]
    public void BuildingAgainReplacesTheRootsChildren()
    {
        Result result = Build($"""
            <StackPanel x:Class="App.Twice" {Xmlns}>
              <Label Text="A"/>
              <Label Text="B"/>
            </StackPanel>
            """);

        var panel = (StackPanel)result.Create("App.Twice");

        // as Hot Reload does
        panel.GetType().GetMethod("InitializeComponent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(panel, null);

        Assert.Equal(2, panel.Children.Count);
    }

    [Theory]
    [InlineData("<Buton/>", "ZF2003", 2)]
    [InlineData("<Button Txt=\"x\"/>", "ZF2004", 2)]
    [InlineData("<Button Width=\"wide\"/>", "ZF2004", 2)]
    [InlineData("<StackPanel Spacing=\"wide\"/>", "ZF2005", 2)]
    [InlineData("<StackPanel Orientation=\"Diagonal\"/>", "ZF2005", 2)]
    [InlineData("<Label TextColor=\"@Accent\"/>", "ZF2005", 2)]
    [InlineData("<Button Click=\"Nowhere\"/>", "ZF2007", 2)]
    [InlineData("<Label Text=\"{Bind Missing.Text}\"/>", "ZF2008", 2)]
    public void AMistakeIsReportedAtItsLine(string element, string id, int line)
    {
        Result result = Build($"<StackPanel x:Class=\"App.Wrong\" {Xmlns}>\n  {element}\n</StackPanel>");

        Diagnostic error = Assert.Single(result.Errors, d => d.Id.StartsWith("ZF", StringComparison.Ordinal));

        Assert.Equal(id, error.Id);
        Assert.Equal(line, error.Location.GetLineSpan().StartLinePosition.Line + 1);
        Assert.EndsWith("Main.zfml", error.Location.GetLineSpan().Path);
    }

    [Fact]
    public void ANameUsedTwiceIsReported()
    {
        Result result = Build($"""
            <StackPanel x:Class="App.Twins" {Xmlns}>
              <Label x:Name="Same"/>
              <Label x:Name="Same"/>
            </StackPanel>
            """);

        Assert.Contains(result.Errors, d => d.Id == "ZF2009");
    }

    [Fact]
    public void MarkupWithoutAClassIsReported()
    {
        Result result = Build($"<StackPanel {Xmlns}/>");

        Assert.Contains(result.Errors, d => d.Id == "ZF2002");
    }

    [Fact]
    public void BrokenXmlIsReported()
    {
        Result result = Build($"<StackPanel x:Class=\"App.Broken\" {Xmlns}>\n  <Label>\n</StackPanel>");

        Assert.Contains(result.Errors, d => d.Id == "ZF2001");
    }
}
