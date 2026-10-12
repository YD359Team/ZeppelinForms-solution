using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeppelinForms.Analyzers;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.UnitTests;

/// <summary>The generated list of an assembly's controls: what is in it, what is left
/// out, and that it compiles.</summary>
public class StyleTypeRegistrationGeneratorTests
{
    private static (string? Source, ImmutableArray<Diagnostic> Diagnostics) Run(string code)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(UIElement).Assembly.Location));

        CSharpCompilation compilation = CSharpCompilation.Create(
            "Library",
            [CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(LanguageVersion.Latest))],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver
            .Create(new StyleTypeRegistrationGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);

        string? source = driver.GetRunResult().GeneratedTrees.FirstOrDefault()?.ToString();

        return (source, output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToImmutableArray());
    }

    [Fact]
    public void TheControlsOfTheAssemblyAreListed()
    {
        var (source, errors) = Run("""
            using ZeppelinForms.Forms.Controls;
            namespace App;
            public class Gauge : Border { }
            internal sealed class FancyButton : Button { }
            public class Outer { public class NestedPanel : StackPanel { } }
            """);

        Assert.Empty(errors);
        Assert.NotNull(source);
        Assert.Contains("typeof(global::App.Gauge)", source, StringComparison.Ordinal);
        Assert.Contains("typeof(global::App.FancyButton)", source, StringComparison.Ordinal);
        Assert.Contains("typeof(global::App.Outer.NestedPanel)", source, StringComparison.Ordinal);
        Assert.Contains("RunClassConstructor(typeof(global::App.Gauge).TypeHandle)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WhatTheAssemblyCannotNameIsLeftOut()
    {
        var (source, errors) = Run("""
            using ZeppelinForms.Forms.Controls.Text;
            namespace App;
            public class Visible : Label { }
            public class Holder { private class Hidden : Label { } protected class Shielded : Label { } }
            public class Generic<T> : Label { }
            public class Model { }
            """);

        Assert.Empty(errors);
        Assert.Contains("global::App.Visible", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Hidden", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Shielded", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Generic", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Model", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAssemblyWithoutControlsGetsNothing()
    {
        var (source, _) = Run("namespace App; public class Model { }");

        Assert.Null(source);
    }

    [Fact]
    public void APartialControlIsListedOnce()
    {
        var (source, errors) = Run("""
            namespace App;
            public partial class Card : ZeppelinForms.Forms.Controls.Border { }
            public partial class Card { }
            public partial class Card : System.IDisposable { public void Dispose() { } }
            """);

        Assert.Empty(errors);
        Assert.Equal(2, source!.Split("global::App.Card").Length - 1);
    }
}
