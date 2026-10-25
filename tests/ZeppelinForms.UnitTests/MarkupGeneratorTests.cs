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
        public ImmutableArray<Diagnostic> Warnings { get; init; } = [];

        public object Create(string type) => Activator.CreateInstance(Assembly!.GetType(type, throwOnError: true)!)!;
    }

    private static int s_build;

    private static Result Build(string markup, string code = "", params string[] styleSheets)
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
            [new MarkupFile("/app/Main.zfml", markup), .. styleSheets.Select(path => new MarkupFile(path, string.Empty))],
            parse);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> generated);

        string? source = driver.GetRunResult().GeneratedTrees.FirstOrDefault()?.ToString();

        ImmutableArray<Diagnostic> errors = [.. generated.Concat(output.GetDiagnostics()).Where(d => d.Severity == DiagnosticSeverity.Error)];
        ImmutableArray<Diagnostic> warnings = [.. generated.Where(d => d.Severity == DiagnosticSeverity.Warning)];

        if (!errors.IsEmpty) return new Result(null, errors, source) { Warnings = warnings };

        using var stream = new MemoryStream();
        var emitted = output.Emit(stream);

        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));

        return new Result(Assembly.Load(stream.ToArray()), errors, source) { Warnings = warnings };
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

    [Fact]
    public void PaletteColorsAreNamedAsCodeNamesThem()
    {
        Result result = Build($"""
            <StackPanel x:Class="App.Palette" Background="Colors.RoyalBlue" {Xmlns}>
              <Label Text="a" TextColor="MediaColors.Telegram"/>
              <Label Text="b" TextColor="dark-gray"/>
            </StackPanel>
            """);

        Assert.Empty(result.Errors);

        var panel = (StackPanel)result.Create("App.Palette");
        Assert.Equal(Colors.RoyalBlue, panel.Background);
        Assert.Equal(MediaColors.Telegram, ((Label)panel.Children[0]).TextColor);
        Assert.Equal(Colors.DarkGray, ((Label)panel.Children[1]).TextColor);
    }

    [Theory]
    [InlineData("<Buton/>", "ZF2003", 2)]
    [InlineData("<Button Txt=\"x\"/>", "ZF2004", 2)]
    [InlineData("<Button Width=\"wide\"/>", "ZF2005", 2)]
    [InlineData("<StackPanel Spacing=\"wide\"/>", "ZF2005", 2)]
    [InlineData("<StackPanel Orientation=\"Diagonal\"/>", "ZF2005", 2)]
    [InlineData("<Label TextColor=\"@Accent\"/>", "ZF2005", 2)]
    [InlineData("<Label TextColor=\"MediaColors.Myspace\"/>", "ZF2005", 2)]
    [InlineData("<Label TextColor=\"Brushes.Red\"/>", "ZF2005", 2)]
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

    // ===== commands =====

    [Fact]
    public void ACommandComesFromTheClass()
    {
        Result result = Build($"""
            <StackPanel x:Class="App.Editor" {Xmlns}>
              <Button x:Name="SaveButton" Text="Save" Command="Save" CommandParameter="draft"/>
            </StackPanel>
            """, """
            namespace App;
            public partial class Editor
            {
                public object? Saved;
                public ZeppelinForms.Input.Commands.Command Save { get; }
                public Editor()
                {
                    Save = new(p => Saved = p);
                    InitializeComponent();
                }
            }
            """);

        Assert.Empty(result.Errors);
        Assert.Contains("e1.Command = this.Save;", result.Source, StringComparison.Ordinal);

        object editor = result.Create("App.Editor");
        var button = Field<Button>(editor, "SaveButton");

        Assert.Same(editor.GetType().GetProperty("Save")!.GetValue(editor), button.Command);

        button.Command!.Execute(button.CommandParameter);
        Assert.Equal("draft", Field<object>(editor, "Saved"));
    }

    [Fact]
    public void AStaticCommandOfAnotherTypeIsNamedWithItsType()
    {
        Result result = Build($"""
            <StackPanel x:Class="App.Shell" {Xmlns}>
              <Button x:Name="Quit" Command="AppCommands.Quit"/>
            </StackPanel>
            """, """
            namespace App;
            public static class AppCommands
            {
                public static readonly ZeppelinForms.Input.Commands.Command Quit = new(() => { });
            }
            """);

        Assert.Empty(result.Errors);
        Assert.Contains("e1.Command = global::App.AppCommands.Quit;", result.Source, StringComparison.Ordinal);

        object shell = result.Create("App.Shell");
        Assert.NotNull(Field<Button>(shell, "Quit").Command);
    }

    [Fact]
    public void ACommandTheMarkupCantFindIsReportedAtItsLine()
    {
        Result result = Build($"""
            <StackPanel x:Class="App.Editor" {Xmlns}>
              <Button Command="Missing"/>
              <Button Command="Count"/>
              <Button Command="Nowhere.Save"/>
            </StackPanel>
            """, """
            namespace App;
            public partial class Editor { public int Count; }
            """);

        Diagnostic[] errors = [.. result.Errors.Where(d => d.Id == "ZF2011")];
        Assert.Equal(3, errors.Length);

        Assert.Equal([2, 3, 4], errors.Select(e => e.Location.GetLineSpan().StartLinePosition.Line + 1));
        Assert.Contains("int", errors[1].GetMessage(), StringComparison.Ordinal);
        Assert.Contains("Nowhere", errors[2].GetMessage(), StringComparison.Ordinal);
    }

    // ===== style sheets =====

    [Fact]
    public void AStyleSheetInTheMarkupStylesTheElement()
    {
        Result result = Build($$"""
            <StackPanel x:Class="App.Styled" {{Xmlns}}>
              <x:Styles>
                $pad: 14 6;
                Button.wide { Padding: $pad; &amp;:hover { Padding: 16 8; } }
              </x:Styles>
              <Button Classes="wide"/>
            </StackPanel>
            """);

        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);

        var panel = (StackPanel)result.Create("App.Styled");

        Assert.Equal(2, panel.Styles.Count);

        // in a window the button takes the sheet's padding
        var form = new Form { Size = new Size(200, 200), Content = panel };
        new ZeppelinForms.Headless.HeadlessPlatform().CreateWindow(form);
        Assert.Equal(new Thickness(14, 6), panel.Children[0].Padding);

        // the sheet's lines are those of the markup
        Assert.Equal("Main.zfml:4", panel.Styles[0].Source);

        // built again, as by Hot Reload: the old sheet goes before the new comes
        panel.GetType().GetMethod("InitializeComponent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(panel, null);
        Assert.Equal(2, panel.Styles.Count);
    }

    [Fact]
    public void AStyleSheetInTheMarkupIsCheckedAtItsLines()
    {
        Result result = Build($$"""
            <StackPanel x:Class="App.Checked" {{Xmlns}}>
              <x:Styles>
                Button { Padding 4; }
                Buton { Padding: 4; }
                Label { Nothing: 1; }
                Label { Padding: $none; }
              </x:Styles>
            </StackPanel>
            """);

        int LineOf(Diagnostic d) => d.Location.GetLineSpan().StartLinePosition.Line + 1;

        Diagnostic[] errors = [.. result.Errors.Where(d => d.Id == "ZF2012")];
        Assert.Equal([3, 6], errors.Select(LineOf));
        Assert.Contains("Property: value", errors[0].GetMessage(), StringComparison.Ordinal);
        Assert.Contains("$none", errors[1].GetMessage(), StringComparison.Ordinal);

        Diagnostic[] warnings = [.. result.Warnings.Where(d => d.Id == "ZF2013")];
        Assert.Equal([4, 5], warnings.Select(LineOf));
        Assert.Contains("'Buton'", warnings[0].GetMessage(), StringComparison.Ordinal);
        Assert.Contains("'Nothing'", warnings[1].GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void AStyleSheetInCDataKeepsItsColumns()
    {
        Result result = Build($$"""
            <StackPanel x:Class="App.Raw" {{Xmlns}}>
              <x:Styles><![CDATA[StackPanel > Buton { Padding: 4; }]]></x:Styles>
            </StackPanel>
            """);

        Assert.Empty(result.Errors);

        Diagnostic warning = Assert.Single(result.Warnings, d => d.Id == "ZF2013");
        LinePosition start = warning.Location.GetLineSpan().StartLinePosition;

        // "  <x:Styles><![CDATA[": the sheet starts at the 22nd column of the 2nd line
        Assert.Equal(2, start.Line + 1);
        Assert.Equal(22, start.Character + 1);

        var panel = (StackPanel)result.Create("App.Raw");
        Assert.Single(panel.Styles);
    }

    [Fact]
    public void AStyleSheetIsLinkedFromTheAssets()
    {
        string markup = $"""
            <StackPanel x:Class="App.Linked" StyleSheet="Styles/app.zss; Styles/extra.zss" {Xmlns}/>
            """;

        Result found = Build(markup, "", "/app/Assets/Styles/app.zss", "/app/Assets/Styles/extra.zss");

        Assert.Empty(found.Errors);
        Assert.Empty(found.Warnings);
        Assert.Contains("this.Styles.Load(global::System.IO.Path.Combine(global::ZeppelinForms.Assets.Root, \"Styles/app.zss\")",
            found.Source, StringComparison.Ordinal);

        // a sheet the project doesn't have may reach the assets another way: a warning
        Result missing = Build(markup, "", "/app/Assets/Styles/app.zss");

        Diagnostic warning = Assert.Single(missing.Warnings);
        Assert.Equal("ZF2014", warning.Id);
        Assert.Contains("Styles/extra.zss", warning.GetMessage(), StringComparison.Ordinal);
    }
}
