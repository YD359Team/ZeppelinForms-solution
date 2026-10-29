using Xunit;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.UnitTests;

/// <summary>0.19.1: a style sheet path that leads nowhere throws in an application,
/// and is a diagnostic in the previewer.</summary>
/// <remarks>In the collection of the other sheet tests: <see cref="App.IsDesignMode"/>
/// is static, and a test that turns it on must not overlap one that loads sheets.</remarks>
[Collection("Platform")]
public class StyleSheetPathTests
{
    private static string MissingSheet() =>
        Path.Combine(Path.GetTempPath(), "zf-missing-" + Guid.NewGuid().ToString("N"), "app.zss");

    [Fact]
    public void AMissingFileThrowsWithTheFullPath()
    {
        string path = MissingSheet();

        FileNotFoundException e = Assert.Throws<FileNotFoundException>(() => StyleSheet.Load(path));

        Assert.Equal(Path.GetFullPath(path), e.FileName);
    }

    [Fact]
    public void LoadingIntoStylesThrowsAndAddsNothing()
    {
        var form = new Form();
        int before = form.Styles.Count;

        Assert.Throws<FileNotFoundException>(() => form.Styles.Load(MissingSheet()));
        Assert.Equal(before, form.Styles.Count);
    }

    [Fact]
    public void InThePreviewerAMissingFileIsADiagnostic()
    {
        string path = MissingSheet();
        bool previous = App.IsDesignMode;

        App.IsDesignMode = true;

        try
        {
            StyleSheet sheet = StyleSheet.Load(path);

            Assert.Empty(sheet.Styles);
            Assert.True(sheet.HasErrors);

            // still the sheet's file: a watcher follows it, and the sheet loads once it is saved
            Assert.Contains(Path.GetFullPath(path), sheet.Files);
        }
        finally
        {
            App.IsDesignMode = previous;
        }
    }

    [Fact]
    public void AMissingImportIsStillADiagnostic()
    {
        string folder = Path.Combine(Path.GetTempPath(), "zf-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            string path = Path.Combine(folder, "main.zss");
            File.WriteAllText(path, "@import \"nowhere.zss\";\nButton { CornerRadius: 4; }");

            StyleSheet sheet = StyleSheet.Load(path);

            Assert.True(sheet.HasErrors);
            Assert.NotEmpty(sheet.Styles);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}