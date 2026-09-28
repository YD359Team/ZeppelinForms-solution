using Android.App;
using Android.Content.Res;
using ZeppelinForms.Forms;

// ZeppelinForms.Assets and Activity.Assets (AssetManager) are different things
// with the same name, and inside this namespace Assets resolves to the former
using ZfAssets = ZeppelinForms.Assets;

namespace ZeppelinForms.Android;

/// <summary>
/// Starting an application on Android in one line. Does what would otherwise have
/// to be repeated in every activity: unpacks the APK resources into the file system,
/// creates the platform and brings up App.
/// </summary>
public static class AndroidApp
{
    /// <param name="mainForm">Exactly a factory, not a ready form: the form's
    /// constructor already measures text, and the measurer appears only after
    /// the platform is created.</param>
    /// <param name="assets">Resource paths relative to the Assets folder — the same
    /// ones that go into Image.LoadAsset. There is no need to name a font: Android
    /// has system fonts, and Skia finds them itself — unlike the browser, which has
    /// no fonts of its own at all.</param>
    /// <returns>The created platform. Through it the application subscribes to the
    /// lifecycle: Paused, Resumed and Saving — the last chance to save the state
    /// before the system may kill the process.</returns>
    public static AndroidPlatform Run(Activity activity, Func<Form> mainForm, IEnumerable<string>? assets = null)
    {
        string files = activity.FilesDir?.AbsolutePath
            ?? throw new InvalidOperationException("FilesDir is unavailable.");

        ZfAssets.Root = Path.Combine(files, "Assets");

        // before the platform is created: it registers the text measurer,
        // and the form goes for resources already in its constructor
        if (assets is not null)
            Unpack(activity, assets);

        AndroidPlatform platform = AndroidPlatform.Create(activity);

        App app = new(platform) { MainForm = mainForm() };
        app.Run();

        return platform;
    }

    private static void Unpack(Activity activity, IEnumerable<string> names)
    {
        AssetManager manager = activity.Assets
            ?? throw new InvalidOperationException("AssetManager is unavailable.");

        Directory.CreateDirectory(ZfAssets.Root);

        foreach (string name in names)
        {
            string target = Path.Combine(ZfAssets.Root, name);

            if (Path.GetDirectoryName(target) is { Length: > 0 } directory)
                Directory.CreateDirectory(directory);

            // the name without a prefix: AndroidAssetsPrefix ("Assets" by default)
            // is cut off during packaging, and inside the APK the resource lies at
            // the root of assets under the same relative name LoadAsset expects
            using Stream source = OpenAsset(manager, name);
            using FileStream destination = File.Create(target);

            source.CopyTo(destination);
        }
    }

    /// <summary>Open an APK resource, and on failure say what is actually there.
    /// The layout inside the APK depends on the build properties, and a bare
    /// FileNotFoundException says nothing about it.</summary>
    private static Stream OpenAsset(AssetManager manager, string name)
    {
        try
        {
            return manager.Open(name);
        }
        catch (Java.IO.FileNotFoundException)
        {
            string[] actual = manager.List(string.Empty) ?? [];

            throw new FileNotFoundException(
                $"Resource \"{name}\" was not found in the APK. The root of assets contains: " +
                $"{(actual.Length == 0 ? "nothing" : string.Join(", ", actual))}.");
        }
    }
}