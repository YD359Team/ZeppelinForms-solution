using Xunit;
using ZeppelinForms.Drawing;

namespace ZeppelinForms.UnitTests;

/// <summary>0.17.0: <see cref="FontFallbacks"/> — the application's own fallback fonts,
/// searched before the system's: the only fallback a browser has.</summary>
[Collection("Platform")]
public class FontFallbacksTests
{
    private static void WithCleanList(Action test)
    {
        string[] before = [.. FontFallbacks.Paths];
        FontFallbacks.Clear();

        try
        {
            test();
        }
        finally
        {
            FontFallbacks.Clear();
            foreach (string path in before) FontFallbacks.Add(path);
        }
    }

    [Fact]
    public void FilesAreSearchedInTheOrderAddedAndAddedOnce() => WithCleanList(() =>
    {
        FontFallbacks.Add("/fonts/Hebrew.ttf");
        FontFallbacks.Add("/fonts/Arabic.ttf");
        FontFallbacks.Add("/fonts/Hebrew.ttf");

        Assert.Equal(["/fonts/Hebrew.ttf", "/fonts/Arabic.ttf"], FontFallbacks.Paths);
    });

    [Fact]
    public void AChangeReplacesTheListWhole() => WithCleanList(() =>
    {
        // the text backend notices a change by reference: a list read before stays as it was
        IReadOnlyList<string> before = FontFallbacks.Paths;

        FontFallbacks.Add("/fonts/Hebrew.ttf");

        Assert.NotSame(before, FontFallbacks.Paths);
        Assert.Empty(before);
    });

    [Fact]
    public void ClearLeavesOnlyTheSystemsFallback() => WithCleanList(() =>
    {
        FontFallbacks.Add("/fonts/Hebrew.ttf");
        FontFallbacks.Clear();

        Assert.Empty(FontFallbacks.Paths);
    });

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyPathThrows(string path) => WithCleanList(() =>
        Assert.Throws<ArgumentException>(() => FontFallbacks.Add(path)));
}