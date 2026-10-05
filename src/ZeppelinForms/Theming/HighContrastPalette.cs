using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Theming;

/// <summary>The colors of a high contrast theme, by the roles every system
/// names: the page and its text, a button and its text, the selection and its
/// text, unavailable text, a link.</summary>
/// <remarks>
/// The roles are those of Windows' system colors and of the CSS system colors
/// forced-colors mode uses — the same set under different names. The user picks
/// the colors in the system; the theme only puts them in their places. Every pair
/// was chosen by the user to be readable, so the theme never mixes them: text of
/// one role on the background of another is exactly what high contrast must not do.
/// </remarks>
public sealed record HighContrastPalette(
    Color Window,
    Color WindowText,
    Color ButtonFace,
    Color ButtonText,
    Color Highlight,
    Color HighlightText,
    Color GrayText,
    Color Hotlight)
{
    /// <summary>Windows' "Night sky" — the classic High Contrast Black: white text
    /// on black, a cyan selection, yellow links, green unavailable text.</summary>
    public static HighContrastPalette Black { get; } = new(
        Window: new Color(0x00, 0x00, 0x00),
        WindowText: new Color(0xFF, 0xFF, 0xFF),
        ButtonFace: new Color(0x00, 0x00, 0x00),
        ButtonText: new Color(0xFF, 0xFF, 0xFF),
        Highlight: new Color(0x1A, 0xEB, 0xFF),
        HighlightText: new Color(0x00, 0x00, 0x00),
        GrayText: new Color(0x3F, 0xF2, 0x3F),
        Hotlight: new Color(0xFF, 0xFF, 0x00));

    /// <summary>Windows' "Desert" — the classic High Contrast White: black text on
    /// white, a dark purple selection, dark blue links, dark red unavailable text.</summary>
    public static HighContrastPalette White { get; } = new(
        Window: new Color(0xFF, 0xFF, 0xFF),
        WindowText: new Color(0x00, 0x00, 0x00),
        ButtonFace: new Color(0xFF, 0xFF, 0xFF),
        ButtonText: new Color(0x00, 0x00, 0x00),
        Highlight: new Color(0x37, 0x00, 0x6E),
        HighlightText: new Color(0xFF, 0xFF, 0xFF),
        GrayText: new Color(0x60, 0x00, 0x00),
        Hotlight: new Color(0x00, 0x00, 0x9F));
}