using ZeppelinForms.Drawing;

namespace MyApp;

/// <summary>The application's icons: strokes on a 24×24 grid, drawn in the color of the
/// text beside them. Paths of SVG — copy one from an icon set, or draw your own.</summary>
internal static class Icons
{
    private static IconSource Line(string path) => IconSource.FromPath(path, 1.5f);

    public static IconSource Home { get; } = Line("M3 11 L12 3 L21 11 M5 9 V21 H19 V9 M10 21 V15 H14 V21");
    public static IconSource Settings { get; } = Line("M12 8.5 A3.5 3.5 0 1 0 12 15.5 A3.5 3.5 0 1 0 12 8.5 Z M12 2 V5 M12 19 V22 M2 12 H5 M19 12 H22 M4.9 4.9 L7 7 M17 17 L19.1 19.1 M4.9 19.1 L7 17 M17 7 L19.1 4.9");
    public static IconSource Add { get; } = Line("M12 5 V19 M5 12 H19");
    public static IconSource Star { get; } = Line("M12 3 L14.8 8.8 L21 9.6 L16.5 14 L17.6 20.3 L12 17.3 L6.4 20.3 L7.5 14 L3 9.6 L9.2 8.8 Z");
}
