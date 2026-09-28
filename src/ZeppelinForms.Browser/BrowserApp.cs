using ZeppelinForms.Drawing;
using ZeppelinForms.Forms;

namespace ZeppelinForms.Browser;

/// <summary>
/// Starting an application in a browser in one line. Gathers together what
/// otherwise has to be repeated in every Program.cs: downloading resources into
/// the file system, creating the platform, substituting the font and catching
/// an initialization failure.
/// </summary>
public static class BrowserApp
{
    /// <param name="mainForm">Exactly a factory, not a ready form: the form's
    /// constructor already measures text, and the measurer appears only after the
    /// platform is created. A ready object would be built before the call —
    /// in the arguments.</param>
    /// <param name="font">The path to the font file — also its address on the server.
    /// null leaves Font.Default as is, and text will be drawn with the fallback
    /// typeface: a browser has no fonts of its own for Skia.</param>
    /// <param name="preload">Other files that will be needed synchronously:
    /// pictures for Image.LoadAsset and anything else from disk.</param>
    /// <param name="fontFamily">The family name. By default it is taken
    /// from the file name — "Inter-Regular.ttf" gives "Inter".</param>
    public static async Task RunAsync(
        Func<Form> mainForm,
        string? font = null,
        IEnumerable<string>? preload = null,
        string? fontFamily = null,
        float fontSize = 14f,
        string canvasId = "zf-canvas")
    {
        try
        {
            List<string> files = [];

            if (font is not null)
                files.Add(font);

            if (preload is not null)
                files.AddRange(preload);

            // downloaded before the platform is created: it registers the text
            // measurer, and that one goes for the font on the very first access
            if (files.Count > 0)
                await BrowserPlatform.PreloadAsync([.. files]);

            BrowserPlatform platform = BrowserPlatform.Create(canvasId);

            if (font is not null)
            {
                Font.Default = new Font(fontFamily ?? FamilyFromPath(font), fontSize)
                    .WithFile(font);
            }

            // the form is built only now: the text measurer
            // and the default font are already in place
            App app = new(platform) { MainForm = mainForm() };
            app.Run();
        }
        catch (Exception exception)
        {
            // without this an initialization failure is visible only in the browser
            // Console, while the page stays white and hints at nothing
            Console.WriteLine($"ZeppelinForms: startup failed. {exception}");
            throw;
        }
    }

    /// <summary>"/fonts/Inter-Regular.ttf" -> "Inter". The family name is needed
    /// only to compare typefaces inside the cache: the font itself is taken
    /// by path, and no system lookup happens.</summary>
    private static string FamilyFromPath(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        int dash = name.IndexOf('-');

        return dash > 0 ? name[..dash] : name;
    }
}