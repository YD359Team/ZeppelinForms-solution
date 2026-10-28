using ZeppelinForms.Drawing;

namespace ZF_SharedLib.Showcase;

/// <summary>The showcase's icons: simple strokes on a 24×24 grid, drawn in the
/// color of the text beside them.</summary>
internal static class Icons
{
    private static IconSource Line(string path) => IconSource.FromPath(path, 1.5f);

    public static IconSource Home { get; } = Line("M3 11 L12 3 L21 11 M5 9 V21 H19 V9 M10 21 V15 H14 V21");
    public static IconSource Button { get; } = Line("M3 7 H21 V17 H3 Z M8 12 H16");
    public static IconSource Check { get; } = Line("M4 4 H20 V20 H4 Z M8 12 L11 15 L16 9");
    public static IconSource Text { get; } = Line("M4 6 V4 H20 V6 M12 4 V20 M9 20 H15");
    public static IconSource Calendar { get; } = Line("M3 5 H21 V21 H3 Z M3 10 H21 M8 3 V7 M16 3 V7");
    public static IconSource Slider { get; } = Line("M3 12 H21 M9 8 V16 M3 6 H8 M3 18 H14");
    public static IconSource List { get; } = Line("M8 6 H21 M8 12 H21 M8 18 H21 M4 6 H5 M4 12 H5 M4 18 H5");
    public static IconSource Table { get; } = Line("M3 4 H21 V20 H3 Z M3 9 H21 M3 14.5 H21 M9 4 V20");
    public static IconSource Tree { get; } = Line("M4 4 H9 V9 H4 Z M12 11 H17 V16 H12 Z M12 18 H17 V22 H12 Z M6.5 9 V20 H12 M6.5 13.5 H12");
    public static IconSource Image { get; } = Line("M3 4 H21 V20 H3 Z M3 16 L9 10 L14 15 L17 12 L21 16 M15 8 H16");
    public static IconSource Drag { get; } = Line("M12 3 V21 M3 12 H21 M12 3 L9 6 M12 3 L15 6 M12 21 L9 18 M12 21 L15 18 M3 12 L6 9 M3 12 L6 15 M21 12 L18 9 M21 12 L18 15");
    public static IconSource Panels { get; } = Line("M3 3 H10 V10 H3 Z M14 3 H21 V10 H14 Z M3 14 H10 V21 H3 Z M14 14 H21 V21 H14 Z");
    public static IconSource Split { get; } = Line("M3 4 H21 V20 H3 Z M11 4 V20 M7 11 L5 12 L7 13 M15 11 L17 12 L15 13");
    public static IconSource Box { get; } = Line("M3 7 L12 3 L21 7 V17 L12 21 L3 17 Z M3 7 L12 11 L21 7 M12 11 V21");
    public static IconSource Tabs { get; } = Line("M3 8 H21 V20 H3 Z M3 8 V4 H9 V8 M9 4 H15 V8");
    public static IconSource Phone { get; } = Line("M7 2 H17 V22 H7 Z M11 18.5 H13");
    public static IconSource Font { get; } = Line("M4 20 L10 4 L16 20 M6.5 14 H13.5 M17 20 V11 M17 13 A3 3 0 1 1 17 17");
    public static IconSource Shapes { get; } = Line("M7 3 A4 4 0 1 0 7 11 A4 4 0 1 0 7 3 Z M14 13 H21 V20 H14 Z M3 21 L7 14 L11 21 Z");
    public static IconSource Chart { get; } = Line("M3 21 H21 M6 21 V12 M11 21 V5 M16 21 V14 M20 21 V9");
    public static IconSource Map { get; } = Line("M3 6 L9 3 L15 6 L21 3 V18 L15 21 L9 18 L3 21 Z M9 3 V18 M15 6 V21");
    public static IconSource Axes { get; } = Line("M5 19 V4 M5 19 H20 M5 19 L14 10 M3 6 L5 4 L7 6 M18 17 L20 19 L18 21 M11 10 H14 V13");
    public static IconSource Brush { get; } = Line("M14 4 L20 10 L11 19 L5 13 Z M5 13 L3 21 L11 19");
    public static IconSource Sparkle { get; } = Line("M12 3 L13.8 10.2 L21 12 L13.8 13.8 L12 21 L10.2 13.8 L3 12 L10.2 10.2 Z");
    public static IconSource Motion { get; } = Line("M3 12 H11 M6 7 H13 M8 17 H14 M17 8 A4 4 0 1 1 17 16 A4 4 0 1 1 17 8 Z");
    public static IconSource Window { get; } = Line("M3 4 H21 V20 H3 Z M3 8 H21 M6 6 H7 M9 6 H10");
    public static IconSource Plug { get; } = Line("M9 3 V8 M15 3 V8 M6 8 H18 V12 A6 6 0 0 1 6 12 Z M12 18 V21");
    public static IconSource Hand { get; } = Line("M8 13 V5 A1.5 1.5 0 0 1 11 5 V11 M11 10 V3.5 A1.5 1.5 0 0 1 14 3.5 V11 M14 10 V5 A1.5 1.5 0 0 1 17 5 V12 M17 9 A1.5 1.5 0 0 1 20 9 V14 A7 7 0 0 1 13 21 A7 7 0 0 1 6 16 L4 12.5 A1.5 1.5 0 0 1 6.5 11 L8 13");
    public static IconSource Calculator { get; } = Line("M5 3 H19 V21 H5 Z M8 6 H16 V10 H8 Z M8 14 H9 M12 14 H13 M16 14 H17 M8 18 H9 M12 18 H13 M16 18 H17");
    public static IconSource Gear { get; } = Line("M12 8.5 A3.5 3.5 0 1 0 12 15.5 A3.5 3.5 0 1 0 12 8.5 Z M12 2 V5 M12 19 V22 M2 12 H5 M19 12 H22 M4.9 4.9 L7 7 M17 17 L19.1 19.1 M4.9 19.1 L7 17 M17 7 L19.1 4.9");
    public static IconSource Info { get; } = Line("M12 3 A9 9 0 1 0 12 21 A9 9 0 1 0 12 3 Z M12 10.5 V16.5 M12 7 V8");
    public static IconSource Search { get; } = Line("M10 3 A7 7 0 1 0 10 17 A7 7 0 1 0 10 3 Z M15 15 L21 21");
    public static IconSource Close { get; } = Line("M6 6 L18 18 M18 6 L6 18");
    public static IconSource Heart { get; } = Line("M12 20 L4 12 A4.5 4.5 0 0 1 12 6 A4.5 4.5 0 0 1 20 12 Z");
    public static IconSource Send { get; } = Line("M3 11 L21 3 L13 21 L11 13 Z M11 13 L21 3");
    public static IconSource Trash { get; } = Line("M4 6 H20 M9 6 V3 H15 V6 M6 6 L7 21 H17 L18 6 M10 10 V17 M14 10 V17");
}
