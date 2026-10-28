using System.Text.Json.Nodes;
using Xunit;
using ZeppelinForms.LanguageServer;
using ZeppelinForms.LanguageServer.Markup;
using ZeppelinForms.LanguageServer.Protocol;
using ZeppelinForms.LanguageServer.Styles;
using ZeppelinForms.LanguageServer.Workspace;

namespace ZeppelinForms.UnitTests;

/// <summary>0.19: the language server of .zfml and .zss — what it reads around a position,
/// what it offers there, and the problems it finds — over a project on the disk.</summary>
public sealed class LanguageServerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "zf-ls-" + Guid.NewGuid().ToString("N"));

    private const string ViewModel = """
        using System.ComponentModel;

        namespace Shop.ViewModels;

        public sealed class Customer
        {
            public string Name { get; set; } = "";
        }

        public sealed class OrderViewModel
        {
            public string Status { get; set; } = "";
            public Customer Customer { get; } = new();
            public ZeppelinForms.Input.Commands.Command Save { get; } = new(() => { });
        }
        """;

    private const string Code = """
        using ZeppelinForms.Input.Mouse;

        namespace Shop;

        public partial class OrderForm
        {
            private void OnCancel(object? sender, MouseClickEventArgs e) { }
        }
        """;

    public LanguageServerTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Views"));

        File.WriteAllText(Path.Combine(_root, "Shop.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
            </Project>
            """);

        File.WriteAllText(Path.Combine(_root, "OrderViewModel.cs"), ViewModel);
        File.WriteAllText(Path.Combine(_root, "Views", "OrderForm.cs"), Code);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    /// <summary>The markup, opened in the editor; the position is where "|" was written.</summary>
    private (string Path, string Text, int Position, MarkupSymbols Symbols) Open(string markup, string file = "OrderForm.zfml")
    {
        int position = markup.IndexOf('|');
        string text = position < 0 ? markup : markup.Remove(position, 1);

        string path = Path.Combine(_root, "Views", file);
        var documents = new Documents();
        documents.Set(path, text);

        ProjectContext project = ProjectContext.For(path)!;
        var symbols = new MarkupSymbols(project.Snapshot(documents, TestContext.Current.CancellationToken));

        return (path, text, position, symbols);
    }

    private static string[] Labels(IReadOnlyList<CompletionEntry> items) => [.. items.Select(i => i.Label)];

    // ===== reading the markup =====

    [Theory]
    [InlineData("<StackPanel><Butt|", "ElementName", "Butt")]
    [InlineData("<StackPanel><Button Te|xt=\"x\"/>", "AttributeName", "Te")]
    [InlineData("<StackPanel><Button |Text=\"x\"/>", "AttributeName", "")]
    [InlineData("<StackPanel><Button Text=\"x\" |/>", "AttributeName", "")]
    [InlineData("<StackPanel><!-- <Butt| -->", "None", "")]
    [InlineData("<StackPanel><Button/>|", "Content", "")]
    public void TheScannerKnowsWhereItIs(string markup, string place, string word)
    {
        int position = markup.IndexOf('|');
        MarkupContext context = MarkupScanner.At(markup.Remove(position, 1), position);

        Assert.Equal(place, context.Place.ToString());
        Assert.Equal(word, context.Word);
    }

    [Fact]
    public void TheScannerReadsTheTagPastThePosition()
    {
        const string markup = "<StackPanel xmlns:vm=\"using:Shop.ViewModels\">\n  <Label Text=\"{Bind |}\" x:DataType=\"vm:OrderViewModel\"/>";
        int position = markup.IndexOf('|');

        MarkupContext context = MarkupScanner.At(markup.Remove(position, 1), position);

        Assert.Equal(MarkupPlace.AttributeValue, context.Place);
        Assert.Equal("{Bind ", context.ValueBefore);
        Assert.Equal("vm:OrderViewModel", context.DataType);
        Assert.Equal("using:Shop.ViewModels", context.NamespaceOf("vm"));
        Assert.Single(context.Ancestors);
    }

    // ===== completion =====

    [Fact]
    public void ElementsAreTheFrameworksControls()
    {
        var (_, text, position, symbols) = Open("<StackPanel Class=\"Shop.OrderForm\">\n  <But|");

        var (items, from) = MarkupCompletion.Complete(text, position, symbols);

        Assert.Contains("Button", Labels(items));
        Assert.Contains("TextBox", Labels(items));
        Assert.Contains("StackPanel.Children", Labels(items));
        Assert.Equal(position - 3, from);
    }

    [Fact]
    public void AttributesAreThePropertiesAndEventsNotWrittenYet()
    {
        var (_, text, position, symbols) = Open("<StackPanel Class=\"Shop.OrderForm\">\n  <Button Text=\"Save\" |/>");

        var (items, _) = MarkupCompletion.Complete(text, position, symbols);
        string[] labels = Labels(items);

        Assert.Contains("Click", labels);
        Assert.Contains("IsEnabled", labels);
        Assert.Contains("x:Name", labels);
        Assert.DoesNotContain("Text", labels);

        Assert.Equal("Click=\"$1\"$0", items.First(i => i.Label == "Click").Insert);
    }

    [Fact]
    public void AValueOfAnEnumIsItsMembers()
    {
        var (_, text, position, symbols) = Open("<StackPanel Class=\"Shop.OrderForm\" Orientation=\"|\"/>");

        var (items, _) = MarkupCompletion.Complete(text, position, symbols);

        Assert.Contains("Horizontal", Labels(items));
        Assert.Contains("Vertical", Labels(items));
    }

    [Fact]
    public void AnEventsValueIsAMethodOfTheClass()
    {
        var (_, text, position, symbols) = Open("<StackPanel Class=\"Shop.OrderForm\">\n  <Button Click=\"|\"/>\n</StackPanel>");

        var (items, _) = MarkupCompletion.Complete(text, position, symbols);

        Assert.Equal(["OnCancel"], Labels(items));
    }

    [Fact]
    public void ABindingsPathGoesThroughTheDataType()
    {
        const string head = "<StackPanel Class=\"Shop.OrderForm\" xmlns:vm=\"using:Shop.ViewModels\" x:DataType=\"vm:OrderViewModel\">\n";

        var (_, text, position, symbols) = Open(head + "  <Label Text=\"{Bind |}\"/>\n</StackPanel>");
        var (items, _) = MarkupCompletion.Complete(text, position, symbols);

        Assert.Equal(["Customer", "Save", "Status"], Labels(items).Order());

        (_, text, position, symbols) = Open(head + "  <Label Text=\"{Bind Customer.N|}\"/>\n</StackPanel>");
        (items, int from) = MarkupCompletion.Complete(text, position, symbols);

        Assert.Equal(["Name"], Labels(items));
        Assert.Equal(position - 1, from);
    }

    [Fact]
    public void ABindingCanStartFromANamedElement()
    {
        var (_, text, position, symbols) = Open(
            "<StackPanel Class=\"Shop.OrderForm\">\n  <TextBox Name=\"Search\"/>\n  <Label Text=\"{Bind Search.Wat|}\"/>\n</StackPanel>");

        var (items, _) = MarkupCompletion.Complete(text, position, symbols);

        Assert.Contains("Watermark", Labels(items));
    }

    [Fact]
    public void ASheetOffersTheControlsPropertiesAndTheTheme()
    {
        var (_, _, _, symbols) = Open("<StackPanel Class=\"Shop.OrderForm\"/>");

        const string sheet = "Button.primary {\n    Back|\n}";
        int position = sheet.IndexOf('|');
        var (items, _) = StyleCompletion.Complete(sheet.Remove(position, 1), position, symbols);

        Assert.Contains("Background", Labels(items));
        Assert.DoesNotContain("Button", Labels(items).Take(5));

        const string value = "Button { Background: @Acc| }";
        position = value.IndexOf('|');
        (items, _) = StyleCompletion.Complete(value.Remove(position, 1), position, symbols);

        Assert.Contains("Accent", Labels(items));

        const string pseudo = "Button:ho| { }";
        position = pseudo.IndexOf('|');
        (items, _) = StyleCompletion.Complete(pseudo.Remove(position, 1), position, symbols);

        Assert.Contains("hover", Labels(items));
    }

    // ===== hover and problems =====

    [Fact]
    public void HoverTellsTheTypeAndTheProperty()
    {
        var (_, text, position, symbols) = Open("<StackPanel Class=\"Shop.OrderForm\">\n  <TextBox Water|mark=\"x\"/>\n</StackPanel>");

        var hover = MarkupHover.At(text, position, symbols);

        Assert.NotNull(hover);
        Assert.Contains("TextBox.Watermark", hover.Value.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void ProblemsAreTheBuildsAtTheirLines()
    {
        var (path, text, _, _) = Open("""
            <StackPanel Class="Shop.OrderForm" xmlns:vm="using:Shop.ViewModels" x:DataType="vm:OrderViewModel">
              <Label Text="{Bind Customer.Town}"/>
              <Button Click="Nowhere"/>
            </StackPanel>
            """);

        var documents = new Documents();
        documents.Set(path, text);

        JsonArray problems = Problems.Of(path, text, ProjectContext.For(path)!.Snapshot(documents, TestContext.Current.CancellationToken));

        Assert.Equal(
            [("ZF2008", 1), ("ZF2007", 2)],
            problems.Select(p => ((string)p!["code"]!, (int)p["range"]!["start"]!["line"]!)).OrderBy(p => p.Item2));
    }

    [Fact]
    public void ASheetsProblemsAreItsShapeAndNames()
    {
        string path = Path.Combine(_root, "app.zss");
        const string sheet = "Button {\n    Orientaton: Vertical;\n}\n";
        File.WriteAllText(path, sheet);

        var documents = new Documents();
        JsonArray problems = Problems.Of(path, sheet, ProjectContext.For(path)!.Snapshot(documents, TestContext.Current.CancellationToken));

        JsonNode problem = Assert.Single(problems)!;
        Assert.Equal(1, (int)problem["range"]!["start"]!["line"]!);
        Assert.Contains("Orientaton", (string)problem["message"]!, StringComparison.Ordinal);
    }
}
