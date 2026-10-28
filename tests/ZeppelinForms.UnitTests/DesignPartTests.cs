using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;
using ZeppelinForms.Analyzers.Design;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;

namespace ZeppelinForms.UnitTests;

/// <summary>0.19: a form's design apart from its behaviour — <c>MainForm.design.cs</c> with
/// the controls and <c>InitializeComponent()</c>, <c>MainForm.cs</c> with what they do. The
/// generator's constructor and Hot Reload hook, ZF0007, and CS8618 that isn't true there.</summary>
[Collection("Platform")]
public class DesignPartTests
{
    private const string Usings = """
        using ZeppelinForms.Forms;
        using ZeppelinForms.Forms.Controls;
        using ZeppelinForms.Forms.Controls.Base;
        using ZeppelinForms.Forms.Controls.Text;
        using ZeppelinForms.Input.Mouse;

        """;

    /// <summary>The usual pair: the design with two fields and a handler wired.</summary>
    private const string Design = Usings + """
        namespace App;

        partial class MainForm
        {
            private Label _title;
            private Button _save;

            private void InitializeComponent()
            {
                Title = "Orders";

                _title = new Label { Text = "Orders" };
                _save = new Button { Text = "Save" };
                _save.Click += OnSave;

                Content = new StackPanel { Children = { _title, _save } };
            }
        }
        """;

    private sealed record Result(
        Compilation Compilation, ImmutableArray<Diagnostic> Diagnostics, ImmutableArray<string> Generated)
    {
        /// <summary>The warnings and errors that stand: not suppressed.</summary>
        public IEnumerable<Diagnostic> Reported => Diagnostics.Where(d => !d.IsSuppressed && d.Severity >= DiagnosticSeverity.Warning);

        public object Create(string type)
        {
            using var stream = new MemoryStream();
            var emitted = Compilation.Emit(stream);

            Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));

            return Activator.CreateInstance(Assembly.Load(stream.ToArray()).GetType(type, throwOnError: true)!)!;
        }
    }

    private static int s_build;

    private static Result Build(params (string Path, string Code)[] files)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(UIElement).Assembly.Location));

        var parse = new CSharpParseOptions(LanguageVersion.Latest);

        CSharpCompilation compilation = CSharpCompilation.Create(
            "Design" + Interlocked.Increment(ref s_build),
            files.Select(file => CSharpSyntaxTree.ParseText(file.Code, parse, file.Path)),
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create([new DesignPartGenerator().AsSourceGenerator()], parseOptions: parse);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);

        ImmutableArray<string> generated = [.. driver.GetRunResult().GeneratedTrees.Select(tree => tree.ToString())];

        var analyzed = output.WithAnalyzers(
            [new DesignPartAnalyzer(), new DesignPartSuppressor()],
            new CompilationWithAnalyzersOptions(
                new AnalyzerOptions([]), onAnalyzerException: null, concurrentAnalysis: false,
                logAnalyzerExecutionTime: false, reportSuppressedDiagnostics: true));

        ImmutableArray<Diagnostic> diagnostics = analyzed.GetAllDiagnosticsAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult();

        return new Result(output, diagnostics, generated);
    }

    [Fact]
    public void TheBehaviourNeedsNoConstructor()
    {
        Result result = Build(
            ("/app/MainForm.design.cs", Design),
            ("/app/MainForm.cs", Usings + """
                namespace App;

                public partial class MainForm : Form
                {
                    public int Saved { get; private set; }

                    private void OnSave(object? sender, MouseClickEventArgs e) => Saved++;
                }
                """));

        Assert.Empty(result.Reported);

        var form = (Form)result.Create("App.MainForm");

        Assert.Equal("Orders", form.Title);
        var panel = Assert.IsType<StackPanel>(form.Content);
        Assert.Equal(2, panel.Children.Count);
        Assert.Equal("Save", Assert.IsType<Button>(panel.Children[1]).Text);
    }

    [Fact]
    public void HotReloadBuildsTheDesignAnew()
    {
        Result result = Build(
            ("/app/MainForm.design.cs", Design),
            ("/app/MainForm.cs", Usings + """
                namespace App;

                public partial class MainForm : Form
                {
                    private void OnSave(object? sender, MouseClickEventArgs e) { }
                }
                """));

        var form = (Form)result.Create("App.MainForm");
        UIElement? before = form.Content;

        typeof(Form).GetMethod("OnHotReload", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, [new HotReloadEventArgs([form.GetType()])]);

        Assert.NotNull(form.Content);
        Assert.NotSame(before, form.Content);

        // an edit elsewhere leaves the form as it is
        UIElement? after = form.Content;

        typeof(Form).GetMethod("OnHotReload", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, [new HotReloadEventArgs([typeof(string)])]);

        Assert.Same(after, form.Content);
    }

    [Fact]
    public void AConstructorOfOnesOwnCallsInitializeComponent()
    {
        Result result = Build(
            ("/app/MainForm.design.cs", Design),
            ("/app/MainForm.cs", Usings + """
                namespace App;

                public partial class MainForm : Form
                {
                    private readonly string _user;

                    public MainForm(string user)
                    {
                        _user = user;
                        InitializeComponent();
                    }

                    public MainForm() : this("guest") { }

                    private void OnSave(object? sender, MouseClickEventArgs e) { }
                }
                """));

        // the generator leaves the constructors to the class; CS8618 of _title and _save
        // is not true — InitializeComponent sets them
        Assert.DoesNotContain(result.Generated, source => source.Contains("public MainForm()"));
        Assert.Empty(result.Reported);
        Assert.Contains(result.Diagnostics, d => d.Id == "CS8618" && d.IsSuppressed);
    }

    [Fact]
    public void AConstructorThatForgetsTheDesignIsWarnedOf()
    {
        Result result = Build(
            ("/app/MainForm.design.cs", Design),
            ("/app/MainForm.cs", Usings + """
                namespace App;

                public partial class MainForm : Form
                {
                    public MainForm()
                    {
                        Title = "Forgot";
                    }

                    private void OnSave(object? sender, MouseClickEventArgs e) { }
                }
                """));

        Diagnostic warning = Assert.Single(result.Reported, d => d.Id == DesignPartAnalyzer.DiagnosticId);
        Assert.Contains("'MainForm'", warning.GetMessage());
        Assert.Contains("MainForm.design.cs", warning.GetMessage());

        // and the fields' CS8618 is true here: it stays
        Assert.Contains(result.Reported, d => d.Id == "CS8618");
    }

    [Fact]
    public void AFieldTheDesignDoesntSetKeepsItsWarning()
    {
        Result result = Build(
            ("/app/MainForm.design.cs", Usings + """
                namespace App;

                partial class MainForm
                {
                    private Label _title;
                    private Label _forgotten;

                    private void InitializeComponent() => _title = new Label();
                }
                """),
            ("/app/MainForm.cs", Usings + """
                namespace App;

                public partial class MainForm : Form
                {
                    public MainForm() => InitializeComponent();
                }
                """));

        // one CS8618 stands — of the field nothing sets; the compiler's CS0169 about it too
        Diagnostic left = Assert.Single(result.Reported, d => d.Id == "CS8618");
        Assert.Contains("_forgotten", left.GetMessage());
    }

    [Fact]
    public void MarkupIsADesignPartToo()
    {
        // what the markup generator compiles MainForm.zfml to, in short
        Result result = Build(
            ("/app/obj/MainForm.zfml.g.cs", Usings + """
                namespace App;

                partial class MainForm
                {
                    [global::System.CodeDom.Compiler.GeneratedCode("ZeppelinForms.Analyzers", null)]
                    private void InitializeComponent() => Title = "Orders";
                }
                """),
            ("/app/MainForm.cs", Usings + """
                namespace App;

                public partial class MainForm : Form
                {
                    public MainForm(int id) => Title = id.ToString();
                }
                """));

        Diagnostic warning = Assert.Single(result.Reported, d => d.Id == DesignPartAnalyzer.DiagnosticId);
        Assert.Contains("its markup", warning.GetMessage());

        // nothing generated: the markup generator writes its own constructor and hook
        Assert.Empty(result.Generated);
    }

    [Fact]
    public void AnInitializeComponentOutsideADesignFileIsJustAMethod()
    {
        Result result = Build(("/app/MainForm.cs", Usings + """
            namespace App;

            public partial class MainForm : Form
            {
                public MainForm() => Title = "Plain";

                private void InitializeComponent() { }
            }
            """));

        Assert.Empty(result.Generated);
        Assert.DoesNotContain(result.Reported, d => d.Id == DesignPartAnalyzer.DiagnosticId);
    }

    [Fact]
    public void AnOwnHotReloadIsLeftAlone()
    {
        Result result = Build(
            ("/app/MainForm.design.cs", Design),
            ("/app/MainForm.cs", Usings + """
                namespace App;

                public partial class MainForm : Form
                {
                    public int Reloads { get; private set; }

                    protected override void OnHotReload(HotReloadEventArgs e) => Reloads++;

                    private void OnSave(object? sender, MouseClickEventArgs e) { }
                }
                """));

        string generated = Assert.Single(result.Generated);
        Assert.Contains("public MainForm()", generated);
        Assert.DoesNotContain("OnHotReload", generated);
        Assert.Empty(result.Reported);
    }

    [Fact]
    public void AControlsDesignWorksAsAFormsDoes()
    {
        Result result = Build(
            ("/app/Card.design.cs", Usings + """
                namespace App;

                partial class Card
                {
                    private Label _caption;

                    private void InitializeComponent()
                    {
                        _caption = new Label { Text = "Card" };

                        // built again by Hot Reload: the old children go first
                        Children.Clear();
                        Children.Add(_caption);
                    }
                }
                """),
            ("/app/Card.cs", Usings + """
                namespace App;

                public partial class Card : StackPanel
                {
                }
                """));

        Assert.Empty(result.Reported);

        var card = (StackPanel)result.Create("App.Card");
        Assert.Equal("Card", Assert.IsType<Label>(Assert.Single(card.Children)).Text);
        Assert.Contains("OnHotReload", Assert.Single(result.Generated));
    }
}
