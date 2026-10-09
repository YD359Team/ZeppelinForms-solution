using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Helpers;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Theming;

/// <summary>The Fluent 2 themes, with the palette of WinUI 3.</summary>
/// <remarks>
/// <para>
/// Opt-in in 0.13: <see cref="Light"/> and <see cref="Dark"/> stay the default,
/// an application switches with <c>App.Theme = Themes.FluentLight</c>.
/// </para>
/// <para>
/// WinUI draws most of its neutral colors as translucent black or white and leaves
/// the blending to the compositor. Here every such color is that blend worked out
/// in advance over the surface it lies on — the page for fills and text, the control
/// fill for strokes — so the themes need neither layers nor alpha from the renderer.
/// Each value names the WinUI resource it comes from. The accent is the Windows
/// default blue, as WinUI picks it: SystemAccentColorDark1 in the light theme and
/// SystemAccentColorLight2 in the dark one; <see cref="Theme.WithAccent(AccentPalette)"/>
/// puts another accent in its place, by the Fluent rule below.
/// </para>
/// <para>
/// The Fluent themes start from the rules shared by all built-in themes and
/// replace the rules of the types Fluent draws differently. A rule replaces the
/// shared one for its type as a whole, so each of them restates what it keeps.
/// </para>
/// </remarks>
public static partial class Themes
{
    public static Theme FluentLight { get; } = BuildFluent(new ThemeColors
    {
        Background = new Color(255, 0xF3, 0xF3, 0xF3),       // SolidBackgroundFillColorBase
        Surface = new Color(255, 0xFB, 0xFB, 0xFB),          // CardBackgroundFillColorDefault
        SurfaceHover = new Color(255, 0xF6, 0xF6, 0xF6),     // ControlFillColorSecondary
        SurfacePressed = new Color(255, 0xF5, 0xF5, 0xF5),   // ControlFillColorTertiary

        Border = new Color(255, 0xE5, 0xE5, 0xE5),           // CardStrokeColorDefault
        BorderFocused = new Color(255, 0x00, 0x5F, 0xB8),    // the accent

        Text = new Color(255, 0x1A, 0x1A, 0x1A),             // TextFillColorPrimary
        TextSecondary = new Color(255, 0x5C, 0x5C, 0x5C),    // TextFillColorSecondary
        TextDisabled = new Color(255, 0x9B, 0x9B, 0x9B),     // TextFillColorDisabled
        TextOnAccent = Colors.White,                         // TextOnAccentFillColorPrimary

        Accent = new Color(255, 0x00, 0x5F, 0xB8),           // AccentFillColorDefault
        AccentHover = new Color(255, 0x18, 0x6E, 0xBE),      // AccentFillColorSecondary
        AccentPressed = new Color(255, 0x31, 0x7D, 0xC4),    // AccentFillColorTertiary

        // a tint of the accent, as in the classic themes: WinUI marks a selected
        // row by a subtle fill and an accent pill, which these controls don't draw
        Selection = new Color(255, 0xC9, 0xDC, 0xEE),
        TextSelection = new Color(255, 0x1A, 0x1A, 0x1A),

        Success = new Color(255, 0x0F, 0x7B, 0x0F),          // SystemFillColorSuccess
        Warning = new Color(255, 0x9D, 0x5D, 0x00),          // SystemFillColorCaution
        Error = new Color(255, 0xC4, 0x2B, 0x1C),            // SystemFillColorCritical

        ScrollTrack = new Color(16, 0, 0, 0),
        ScrollThumb = new Color(0x72, 0, 0, 0),              // ControlStrongFillColorDefault

        ControlFill = new Color(255, 0xFB, 0xFB, 0xFB),      // ControlFillColorDefault
        ControlStroke = new Color(255, 0xEC, 0xEC, 0xEC),    // ControlStrokeColorDefault
        ControlStrokeSecondary = new Color(255, 0xD3, 0xD3, 0xD3), // ControlStrokeColorSecondary
        ControlStrongStroke = new Color(255, 0x8B, 0x8B, 0x8B),    // ControlStrongStrokeColorDefault
        FocusStrokeOuter = new Color(255, 0x1A, 0x1A, 0x1A), // FocusStrokeColorOuter
        FocusStrokeInner = new Color(255, 0xFE, 0xFE, 0xFE), // FocusStrokeColorInner
    }, "FluentLight");

    public static Theme FluentDark { get; } = BuildFluent(new ThemeColors
    {
        Background = new Color(255, 0x20, 0x20, 0x20),       // SolidBackgroundFillColorBase
        Surface = new Color(255, 0x2D, 0x2D, 0x2D),          // CardBackgroundFillColorDefault
        SurfaceHover = new Color(255, 0x32, 0x32, 0x32),     // ControlFillColorSecondary
        SurfacePressed = new Color(255, 0x27, 0x27, 0x27),   // ControlFillColorTertiary

        // WinUI's dark card stroke is translucent black, darker than the page and
        // all but invisible on it. Border also draws separators, list frames and
        // inactive dots here, so it takes the control stroke instead
        Border = new Color(255, 0x3C, 0x3C, 0x3C),           // ControlStrokeColorDefault
        BorderFocused = new Color(255, 0x60, 0xCD, 0xFF),    // the accent

        Text = Colors.White,                                 // TextFillColorPrimary
        TextSecondary = new Color(255, 0xCC, 0xCC, 0xCC),    // TextFillColorSecondary
        TextDisabled = new Color(255, 0x71, 0x71, 0x71),     // TextFillColorDisabled
        TextOnAccent = Colors.Black,                         // TextOnAccentFillColorPrimary

        Accent = new Color(255, 0x60, 0xCD, 0xFF),           // AccentFillColorDefault
        AccentHover = new Color(255, 0x5A, 0xBC, 0xE9),      // AccentFillColorSecondary
        AccentPressed = new Color(255, 0x53, 0xAA, 0xD2),    // AccentFillColorTertiary

        Selection = new Color(255, 0x3C, 0x5D, 0x6C),
        TextSelection = Colors.White,

        Success = new Color(255, 0x6C, 0xCB, 0x5F),          // SystemFillColorSuccess
        Warning = new Color(255, 0xFC, 0xE1, 0x00),          // SystemFillColorCaution
        Error = new Color(255, 0xFF, 0x99, 0xA4),            // SystemFillColorCritical

        ScrollTrack = new Color(16, 255, 255, 255),
        ScrollThumb = new Color(0x8B, 255, 255, 255),        // ControlStrongFillColorDefault

        ControlFill = new Color(255, 0x2D, 0x2D, 0x2D),      // ControlFillColorDefault
        ControlStroke = new Color(255, 0x3C, 0x3C, 0x3C),    // ControlStrokeColorDefault
        ControlStrokeSecondary = new Color(255, 0x41, 0x41, 0x41), // ControlStrokeColorSecondary
        ControlStrongStroke = new Color(255, 0x9F, 0x9F, 0x9F),    // ControlStrongStrokeColorDefault
        FocusStrokeOuter = Colors.White,                     // FocusStrokeColorOuter
        FocusStrokeInner = new Color(255, 0x0D, 0x0D, 0x0D), // FocusStrokeColorInner
    }, "FluentDark");

    /// <summary>Segoe UI Variable, the face of Windows 11 and WinUI; the classic
    /// Segoe UI where it's missing, the system sans-serif off Windows.</summary>
    private static Font FluentFont => new("Segoe UI Variable Text, Segoe UI, sans-serif", 14);

    /// <summary>How a Fluent theme takes an accent, as WinUI does: the shade for its
    /// page — Dark1 on the light one, Light2 on the dark — with hover and press as
    /// that shade at 90 % and 80 % over the page, so both are lighter than rest in
    /// the light theme and darker in the dark one. The text on the accent stays
    /// white and black: the shades are chosen to carry it.</summary>
    private static ThemeColors FluentAccent(ThemeColors colors, AccentPalette palette)
    {
        bool dark = colors.IsDark;
        Color accent = dark ? palette.ForDarkPage() : palette.ForLightPage();

        return colors with
        {
            Accent = accent,
            AccentHover = Color.Lerp(colors.Background, accent, 0.9f),
            AccentPressed = Color.Lerp(colors.Background, accent, 0.8f),
            BorderFocused = accent,
            TextOnAccent = dark ? Colors.Black : Colors.White,
            Selection = Color.Lerp(colors.Surface, accent, dark ? 0.3f : 0.2f),
        };
    }

    private static Theme BuildFluent(ThemeColors colors, string name)
    {
        return Build(colors, name, ThemeMetrics.Fluent, FluentFont, FluentAccent)

            // every control: the two-stroke focus ring, the rounding of small
            // controls, and no border change on hover — Fluent answers hover with
            // the fill. The focus border stays for controls that draw no ring of
            // their own (a combo box, a picker): it shows only while focus is
            // visible, as their ring. Text fields have an underline instead
            .For<InteractiveControl>((control, t) =>
            {
                ThemeColors c = t.Colors;

                control.BorderColor = c.ControlStroke;
                control.FocusBorderColor = c.FocusStrokeOuter;
                control.HoverBorderColor = Colors.Transparent;
                control.CornerRadius = t.Metrics.ControlCornerRadius;

                control.FocusRingColor = c.FocusStrokeOuter;
                control.FocusRingInnerColor = c.FocusStrokeInner;
                control.FocusRingThickness = t.Metrics.FocusStrokeThickness;
                control.FocusRingInnerThickness = t.Metrics.FocusStrokeInnerThickness;
            })

            // a neutral button: control fill, the elevation edge, no ripple —
            // Fluent answers a press with the fill alone. The ring runs flush with
            // the edge: the inset is half the outer stroke
            .For<ButtonBase>((button, t) =>
            {
                ThemeColors c = t.Colors;

                button.BackgroundColor = c.ControlFill;
                button.HoverBackgroundColor = c.SurfaceHover;
                button.PressedBackgroundColor = c.SurfacePressed;
                button.CheckedBackgroundColor = c.Accent;
                button.DisabledBackgroundColor = c.SurfacePressed;
                button.DisabledTextColor = c.TextDisabled;

                button.ElevationBorderColor = c.ControlStrokeSecondary;
                button.FocusRingInset = t.Metrics.FocusStrokeThickness / 2f;

                // the color is kept for code that turns the ripple back on
                button.RippleEnabled = false;
                button.RippleColor = new Color(50, c.Text.R, c.Text.G, c.Text.B);
            })

            // the accent button's lower edge is its own color under black: 40 %
            // in the light theme, 14 % in the dark, as AccentControlElevationBorderBrush
            .For<PrimaryButton>((button, c) =>
            {
                button.BackgroundColor = c.Accent;
                button.HoverBackgroundColor = c.AccentHover;
                button.PressedBackgroundColor = c.AccentPressed;
                button.CheckedBackgroundColor = c.AccentPressed;
                button.DisabledBackgroundColor = c.SurfacePressed;
                button.TextColor = c.TextOnAccent;
                button.DisabledTextColor = c.TextDisabled;
                button.BorderColor = c.Accent;
                button.ElevationBorderColor = c.Accent.Darken(c.IsDark ? 0.14f : 0.4f);
                button.RippleColor = new Color(70, c.TextOnAccent.R, c.TextOnAccent.G, c.TextOnAccent.B);
            })

            // Fluent has no outlined button; the classic one keeps its outline in
            // Fluent's shape, and stays flat — an outline doesn't rise above the page
            .For<SecondaryButton>((button, c) =>
            {
                button.BackgroundColor = Colors.Transparent;
                button.HoverBackgroundColor = c.SurfaceHover;
                button.PressedBackgroundColor = c.SurfacePressed;
                button.CheckedBackgroundColor = c.Accent;
                button.DisabledBackgroundColor = Colors.Transparent;
                button.TextColor = c.Accent;
                button.DisabledTextColor = c.TextDisabled;
                button.BorderColor = c.Accent;
                button.ElevationBorderColor = Colors.Transparent;
                button.RippleColor = new Color(50, c.Accent.R, c.Accent.G, c.Accent.B);
            })

            // nor a danger button: it is drawn as an accent button in the error color
            .For<DangerButton>((button, c) =>
            {
                button.BackgroundColor = c.Error;
                button.HoverBackgroundColor = c.Error.Lighten(0.12f);
                button.PressedBackgroundColor = c.Error.Darken(0.15f);
                button.CheckedBackgroundColor = c.Error.Darken(0.2f);
                button.DisabledBackgroundColor = c.SurfacePressed;
                button.TextColor = c.TextOnAccent;
                button.DisabledTextColor = c.TextDisabled;
                button.BorderColor = c.Error;
                button.ElevationBorderColor = c.Error.Darken(c.IsDark ? 0.14f : 0.4f);
                button.RippleColor = new Color(70, c.TextOnAccent.R, c.TextOnAccent.G, c.TextOnAccent.B);
            })

            .For<ToggleButton>((button, c) =>
            {
                button.BackgroundColor = c.ControlFill;
                button.HoverBackgroundColor = c.SurfaceHover;
                button.PressedBackgroundColor = c.SurfacePressed;

                button.CheckedBackgroundColor = c.Accent;
                button.CheckedHoverBackgroundColor = c.AccentHover;
                button.CheckedPressedBackgroundColor = c.AccentPressed;

                button.DisabledBackgroundColor = c.SurfacePressed;
                button.TextColor = c.Text;
                button.CheckedTextColor = c.TextOnAccent;
                button.DisabledTextColor = c.TextDisabled;
            })

            // a new rule, not a replacement: the shared rules have none for this
            // type, and it applies before those of TextBox, MaskedTextBox and
            // NumericUpDown, which keep their own. Focus is shown by the underline,
            // so the focus border goes
            .For<TextInputControl>((field, c) =>
            {
                field.FocusBorderColor = Colors.Transparent;
                field.UnderlineColor = c.ControlStrongStroke;
                field.FocusUnderlineColor = c.Accent;
                field.FocusUnderlineThickness = 2f;
            })

            // an empty box is an outline and nothing else, hence the strong stroke.
            // Its fill is ControlAltFillColorSecondary, a shade under the control
            // fill — the pressed surface is that shade in both palettes
            .For<CheckBox>((box, t) =>
            {
                ThemeColors c = t.Colors;

                box.BoxSize = 20f;
                box.BoxCornerRadius = t.Metrics.ControlCornerRadius;
                box.BoxBorderWidth = 1f;
                box.BoxBackground = c.SurfacePressed;
                box.BoxBorderColor = c.ControlStrongStroke;
                box.HoverBorderColor = c.TextSecondary;
                box.CheckColor = c.Accent;
                box.CheckGlyphColor = c.TextOnAccent;
            })

            // checked: an accent disk with a dot of the text-on-accent color —
            // the reverse of the classic accent dot in an empty circle
            .For<RadioButton>((radio, c) =>
            {
                radio.CircleSize = 20f;
                radio.CircleBorderWidth = 1f;
                radio.DotSize = 12f;
                radio.CircleBackground = c.SurfacePressed;
                radio.CircleBorderColor = c.ControlStrongStroke;
                radio.HoverBorderColor = c.TextSecondary;
                radio.CheckColor = c.Accent;
                radio.CheckedCircleBackground = c.Accent;
                radio.DotColor = c.TextOnAccent;
            })

            // off: a hollow track with a strong stroke and a thumb in the secondary
            // text color; on: an accent track with a thumb of the text-on-accent color
            .For<ToggleSwitch>((toggle, c) =>
            {
                toggle.TrackSize = new Size(40f, 20f);
                toggle.ThumbInset = 4f;
                toggle.TrackBorderWidth = 1f;
                toggle.OnColor = c.Accent;
                toggle.OffColor = c.SurfacePressed;
                toggle.OffBorderColor = c.ControlStrongStroke;
                toggle.ThumbColor = c.TextSecondary;
                toggle.OnThumbColor = c.TextOnAccent;
            })

            // the rail is the strong fill, the thumb a raised disk on it
            .For<TrackBar>((bar, c) =>
            {
                bar.TrackColor = c.ControlStrongStroke;
                bar.FillColor = c.Accent;
                bar.ThumbColor = c.ControlFill;
                bar.ThumbBorderColor = c.ControlStrokeSecondary;
            })

                        .For<RangeSlider>((slider, c) =>
                        {
                            slider.TrackColor = c.ControlStrongStroke;
                            slider.FillColor = c.Accent;
                            slider.ThumbColor = c.ControlFill;
                            slider.ThumbBorderColor = c.ControlStrokeSecondary;
                        })

            // a selected row reads by a tint rather than the full accent: the rows
            // keep the theme's text color, and dark text on the accent is lost
            .For<ListBox>((list, t) =>
            {
                ThemeColors c = t.Colors;

                list.Background = c.Surface;
                list.SelectionColor = c.Selection;
                list.HoverColor = new Color(24, c.Text.R, c.Text.G, c.Text.B);
                list.BorderColor = c.ControlStroke;
                list.BorderWidth = 1f;
                list.CornerRadius = t.Metrics.ControlCornerRadius;
            })

            // what floats above the content is rounded more than the controls
            .For<MenuList>((menu, t) =>
            {
                ThemeColors c = t.Colors;

                menu.Background = c.Surface;
                menu.DisabledColor = c.TextDisabled;
                menu.HoverColor = c.SurfaceHover;
                menu.SeparatorColor = c.Border;
                menu.CornerRadius = t.Metrics.OverlayCornerRadius;
            });
    }
}