using ZeppelinForms.Drawing.Imaging;

namespace ZeppelinForms;

/// <summary>Built-in resources of the framework.</summary>
public static class Assets
{
    private const string IconResource = "ZeppelinForms.Resources.ZF.ico";

    private static Icon? _logo;

    /// <summary>The ZeppelinForms icon. Read once: the ICO is parsed on first
    /// access, and the same object is returned after that.</summary>
    public static Icon Logo => _logo ??= Icon.FromStream(
        typeof(Assets).Assembly.GetManifestResourceStream(IconResource)
            ?? throw new InvalidOperationException($"Resource {IconResource} not found."));

    /// <summary>The logo as an image of the required size.</summary>
    public static Image LogoImage(int size = 256) => Logo.ToImage(size, size);

    /// <summary>The directory application resources are read from.</summary>
    /// <remarks>
    /// Configurable, because "next to the executable" is an assumption of desktop
    /// platforms. In an APK resources lie inside the archive and are not addressed
    /// by path: the backend copies them into the application directory and puts it
    /// here. The same will be needed where the application is packed into a single
    /// file or launched from a sandbox.
    /// </remarks>
    public static string Root { get; set; } = Path.Combine(AppContext.BaseDirectory, "Assets");
}