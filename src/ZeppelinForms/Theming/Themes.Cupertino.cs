using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Helpers;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Navigation;
using ZeppelinForms.Forms.Controls.Text;

namespace ZeppelinForms.Theming;

/// <summary>The Cupertino themes: iOS as Apple's Human Interface Guidelines draw it — the
/// grouped background with its cards, the system colors, hairline separators, rounded fills
/// instead of outlines, the green switch.</summary>
/// <remarks>
/// <para>
/// The palette is UIKit's semantic colors. Its labels, fills and separators are translucent
/// over whatever lies under them; here each is that blend worked out over the surface it
/// lies on — a card of <c>secondarySystemGroupedBackground</c> — as the Fluent themes do with
/// WinUI's. Each value names the UIKit color it comes from.
/// </para>
/// <para>
/// A neutral <see cref="Button"/> is iOS's gray button — a fill with the label in the
/// accent —, <see cref="PrimaryButton"/> the filled one, <see cref="SecondaryButton"/> the
/// plain one, a label in the accent and nothing round it. A press dims rather than ripples.
/// Text fields are fills, without an outline or an underline. The face is San Francisco
/// where the system has it, and the body is 17, as on an iPhone.
/// </para>
/// </remarks>
public static partial class Themes
{
    public static Theme CupertinoLight { get; } = BuildCupertino(new ThemeColors
    {
        Background = new Color(255, 0xF2, 0xF2, 0xF7),       // systemGroupedBackground
        Surface = Colors.White,                              // secondarySystemGroupedBackground
        SurfaceHover = new Color(255, 0xF2, 0xF2, 0xF7),     // systemGray6
        SurfacePressed = new Color(255, 0xE5, 0xE5, 0xEA),   // systemGray5

        Border = new Color(255, 0xC6, 0xC6, 0xC8),           // separator over white
        BorderFocused = new Color(255, 0x00, 0x7A, 0xFF),    // the accent

        Text = Colors.Black,                                 // label
        TextSecondary = new Color(255, 0x8A, 0x8A, 0x8E),    // secondaryLabel over white
        TextDisabled = new Color(255, 0xC4, 0xC4, 0xC6),     // tertiaryLabel over white
        TextOnAccent = Colors.White,

        Accent = new Color(255, 0x00, 0x7A, 0xFF),           // systemBlue
        AccentHover = new Color(255, 0x26, 0x8E, 0xFF),
        AccentPressed = new Color(255, 0x4D, 0xA2, 0xFF),

        // a selected row is grey in iOS, not a tint of the accent
        Selection = new Color(255, 0xD1, 0xD1, 0xD6),        // systemGray4
        TextSelection = Colors.Black,

        Success = new Color(255, 0x34, 0xC7, 0x59),          // systemGreen
        Warning = new Color(255, 0xFF, 0x95, 0x00),          // systemOrange
        Error = new Color(255, 0xFF, 0x3B, 0x30),            // systemRed

        ScrollTrack = Colors.Transparent,
        ScrollThumb = new Color(0x73, 0, 0, 0),

        ControlFill = new Color(255, 0xEE, 0xEE, 0xEF),      // tertiarySystemFill over white
        ControlStroke = new Color(255, 0xC6, 0xC6, 0xC8),    // separator
        ControlStrokeSecondary = new Color(255, 0xC6, 0xC6, 0xC8),
        ControlStrongStroke = new Color(255, 0xC7, 0xC7, 0xCC),    // systemGray3
        FocusStrokeOuter = new Color(255, 0x00, 0x7A, 0xFF), // the accent: iPadOS' focus ring
        FocusStrokeInner = Colors.White,
    }, "CupertinoLight");

    public static Theme CupertinoDark { get; } = BuildCupertino(new ThemeColors
    {
        Background = Colors.Black,                           // systemGroupedBackground
        Surface = new Color(255, 0x1C, 0x1C, 0x1E),          // secondarySystemGroupedBackground
        SurfaceHover = new Color(255, 0x2C, 0x2C, 0x2E),     // systemGray5
        SurfacePressed = new Color(255, 0x3A, 0x3A, 0x3C),   // systemGray4

        Border = new Color(255, 0x38, 0x38, 0x3A),           // separator over black
        BorderFocused = new Color(255, 0x0A, 0x84, 0xFF),

        Text = Colors.White,                                 // label
        TextSecondary = new Color(255, 0x8D, 0x8D, 0x93),    // secondaryLabel over black
        TextDisabled = new Color(255, 0x46, 0x46, 0x4A),     // tertiaryLabel over black
        TextOnAccent = Colors.White,

        Accent = new Color(255, 0x0A, 0x84, 0xFF),           // systemBlue, dark
        AccentHover = new Color(255, 0x2F, 0x97, 0xFF),
        AccentPressed = new Color(255, 0x08, 0x6A, 0xCC),

        Selection = new Color(255, 0x3A, 0x3A, 0x3C),        // systemGray4
        TextSelection = Colors.White,

        Success = new Color(255, 0x30, 0xD1, 0x58),          // systemGreen, dark
        Warning = new Color(255, 0xFF, 0x9F, 0x0A),          // systemOrange, dark
        Error = new Color(255, 0xFF, 0x45, 0x3A),            // systemRed, dark

        ScrollTrack = Colors.Transparent,
        ScrollThumb = new Color(0x73, 255, 255, 255),

        ControlFill = new Color(255, 0x2C, 0x2C, 0x2E),      // tertiarySystemFill over the card
        ControlStroke = new Color(255, 0x38, 0x38, 0x3A),
        ControlStrokeSecondary = new Color(255, 0x38, 0x38, 0x3A),
        ControlStrongStroke = new Color(255, 0x48, 0x48, 0x4A),    // systemGray3, dark
        FocusStrokeOuter = new Color(255, 0x0A, 0x84, 0xFF),
        FocusStrokeInner = Colors.Black,
    }, "CupertinoDark");

    /// <summary>San Francisco, the face of Apple's systems; Helvetica Neue and the system
    /// sans-serif elsewhere.</summary>
    private static Font CupertinoFont => new("SF Pro Text, SF Pro, Helvetica Neue, Helvetica, Arial, sans-serif", 17);

    /// <summary>How a Cupertino theme takes an accent, as iOS takes a tint: as it is, with
    /// white on it while white reads, black where it doesn't — a yellow tint. Under the
    /// pointer it lightens, pressed it dims toward the card, as an iOS button highlights.
    /// The selection stays grey: in iOS it doesn't follow the tint.</summary>
    private static ThemeColors CupertinoAccent(ThemeColors colors, AccentPalette palette)
    {
        Color accent = palette.Accent;
        Color text = accent.ContrastRatio(Colors.White) >= 3f ? Colors.White : Colors.Black;

        return colors with
        {
            Accent = accent,
            AccentHover = Color.Lerp(accent, Colors.White, 0.15f),
            AccentPressed = Color.Lerp(colors.Surface, accent, 0.7f),
            BorderFocused = accent,
            FocusStrokeOuter = accent,
            TextOnAccent = text,
        };
    }

    private static Theme BuildCupertino(ThemeColors colors, string name)
    {
        return Build(colors, name, ThemeMetrics.Cupertino, CupertinoFont, CupertinoAccent, TypeRamp.Cupertino)

            // no outlines: iOS draws its controls as fills. The focus of a keyboard is a
            // ring of the accent, as on an iPad with one
            .For<InteractiveControl>((control, t) =>
            {
                ThemeColors c = t.Colors;

                control.BorderColor = Colors.Transparent;
                control.HoverBorderColor = Colors.Transparent;
                control.FocusBorderColor = Colors.Transparent;
                control.CornerRadius = t.Metrics.ControlCornerRadius;

                control.FocusRingColor = c.FocusStrokeOuter;
                control.FocusRingThickness = t.Metrics.FocusStrokeThickness;
                control.FocusRingInnerThickness = 0f;
            })

            // the gray button: a fill, and the label in the accent; a press dims the label
            // as iOS highlights it, without a ripple
            .For<ButtonBase>((button, t) =>
            {
                ThemeColors c = t.Colors;

                button.BackgroundColor = c.ControlFill;
                button.HoverBackgroundColor = c.SurfacePressed;
                button.PressedBackgroundColor = c.SurfacePressed;
                button.CheckedBackgroundColor = c.Accent;
                button.DisabledBackgroundColor = c.ControlFill;
                button.DisabledTextColor = c.TextDisabled;
                button.PressedTextColor = c.Accent.WithA(0x80);

                button.ElevationBorderColor = Colors.Transparent;
                button.FocusRingInset = t.Metrics.FocusStrokeThickness / 2f;

                button.RippleEnabled = false;
                button.RippleColor = c.Text.WithA(40);
            })

            .For<Button>((button, c) =>
            {
                button.TextColor = c.Accent;
                button.Padding = new Thickness(16, 8);
            })

            // filled: the accent with white on it, the label dimmed while pressed
            .For<PrimaryButton>((button, c) =>
            {
                button.BackgroundColor = c.Accent;
                button.HoverBackgroundColor = c.AccentHover;
                button.PressedBackgroundColor = c.AccentPressed;
                button.CheckedBackgroundColor = c.AccentPressed;
                button.TextColor = c.TextOnAccent;
                button.PressedTextColor = c.TextOnAccent.WithA(0xB3);
            })

            // plain: the label alone
            .For<SecondaryButton>((button, c) =>
            {
                button.BackgroundColor = Colors.Transparent;
                button.HoverBackgroundColor = c.SurfaceHover;
                button.PressedBackgroundColor = Colors.Transparent;
                button.DisabledBackgroundColor = Colors.Transparent;
                button.TextColor = c.Accent;
            })

            // a destructive action: the red fill, white on it
            .For<DangerButton>((button, c) =>
            {
                button.BackgroundColor = c.Error;
                button.HoverBackgroundColor = Color.Lerp(c.Error, Colors.White, 0.15f);
                button.PressedBackgroundColor = Color.Lerp(c.Surface, c.Error, 0.7f);
                button.CheckedBackgroundColor = Color.Lerp(c.Surface, c.Error, 0.7f);
                button.TextColor = Colors.White;
                button.PressedTextColor = Colors.White.WithA(0xB3);
            })

            .For<ToggleButton>((button, c) =>
            {
                button.CheckedBackgroundColor = c.Accent;
                button.CheckedHoverBackgroundColor = c.AccentHover;
                button.CheckedPressedBackgroundColor = c.AccentPressed;
                button.CheckedTextColor = c.TextOnAccent;
            })

            // fields are fills, rounded, without an underline
            .For<TextInputControl>((field, c) =>
            {
                field.UnderlineColor = Colors.Transparent;
                field.FocusUnderlineColor = Colors.Transparent;
                field.Padding = new Thickness(10, 7);
            })

            .For<TextBox>((box, c) =>
            {
                box.Background = c.ControlFill;
                box.CaretColor = c.Accent;
                box.SelectionColor = c.Accent.WithA(64);
            })

            .For<MaskedTextBox>((box, c) =>
            {
                box.Background = c.ControlFill;
                box.CaretColor = c.Accent;
            })

            .For<NumericUpDown>((numeric, c) =>
            {
                numeric.Background = c.ControlFill;
                numeric.ButtonColor = Colors.Transparent;
                numeric.ButtonHoverColor = c.SurfacePressed;
                numeric.ArrowColor = c.Accent;
            })

            .For<ComboBox>((combo, c) =>
            {
                combo.Background = c.ControlFill;
                combo.PlaceholderColor = c.TextSecondary;
            })

            .For<DateTimePicker>((picker, c) => picker.Background = c.ControlFill)
            .For<TimePicker>((picker, c) => picker.Background = c.ControlFill)
            .For<CheckedComboBox>((combo, c) => combo.Background = c.ControlFill)

            // iOS checks a row with a round mark: the check box is a circle, filled with the
            // accent when checked
            .For<CheckBox>((box, c) =>
            {
                box.BoxSize = 22f;
                box.BoxCornerRadius = new CornerRadius(11f);
                box.BoxBorderWidth = 1.5f;
                box.BoxBackground = Colors.Transparent;
                box.BoxBorderColor = c.ControlStrongStroke;
                box.HoverBorderColor = c.TextSecondary;
                box.CheckColor = c.Accent;
                box.CheckGlyphColor = c.TextOnAccent;
            })

            .For<RadioButton>((radio, c) =>
            {
                radio.CircleSize = 22f;
                radio.CircleBorderWidth = 1.5f;
                radio.DotSize = 8f;
                radio.CircleBackground = Colors.Transparent;
                radio.CircleBorderColor = c.ControlStrongStroke;
                radio.HoverBorderColor = c.TextSecondary;
                radio.CheckColor = c.Accent;
                radio.CheckedCircleBackground = c.Accent;
                radio.DotColor = c.TextOnAccent;
            })

            // the switch of iOS: 51 × 31, green when on — the system's green, not the
            // accent — and a white thumb either way
            .For<ToggleSwitch>((toggle, c) =>
            {
                toggle.TrackSize = new Size(51f, 31f);
                toggle.ThumbInset = 2f;
                toggle.TrackBorderWidth = 0f;
                toggle.OnColor = c.Success;
                toggle.OffColor = c.IsDark ? new Color(255, 0x39, 0x39, 0x3D) : new Color(255, 0xE9, 0xE9, 0xEB);  // systemFill
                toggle.OffBorderColor = Colors.Transparent;
                toggle.ThumbColor = Colors.White;
                toggle.OnThumbColor = Colors.White;
            })

            // a thin grey track, the accent before the thumb, a white thumb with a hairline
            .For<TrackBar>((bar, c) =>
            {
                bar.TrackColor = c.SurfacePressed;
                bar.FillColor = c.Accent;
                bar.ThumbColor = Colors.White;
                bar.ThumbBorderColor = c.Border;
            })

            .For<RangeSlider>((slider, c) =>
            {
                slider.TrackColor = c.SurfacePressed;
                slider.FillColor = c.Accent;
                slider.ThumbColor = Colors.White;
                slider.ThumbBorderColor = c.Border;
            })

            .For<ProgressBar>((bar, c) =>
            {
                bar.FillColor = c.Accent;
                bar.TrackColor = c.SurfacePressed;

                // a bar of two fills, without a frame
                bar.BorderWidth = 0f;
                bar.CornerRadius = new CornerRadius(2f);
            })

            // an inset grouped list: a card on the grouped background, no frame
            .For<ListBox>((list, t) =>
            {
                ThemeColors c = t.Colors;

                list.Background = c.Surface;
                list.SelectionColor = c.Selection;
                list.HoverColor = c.SurfaceHover;
                list.BorderColor = Colors.Transparent;
                list.BorderWidth = 0f;
                list.CornerRadius = t.Metrics.ControlCornerRadius;
            })

            // a context menu: rounded 13, hairlines between the groups, a soft shadow
            .For<MenuList>((menu, t) =>
            {
                ThemeColors c = t.Colors;

                menu.Background = c.IsDark ? new Color(255, 0x25, 0x25, 0x27) : new Color(255, 0xF9, 0xF9, 0xF9);
                menu.DisabledColor = c.TextDisabled;
                menu.HoverColor = c.SurfacePressed;
                menu.SeparatorColor = c.Border;
                menu.CornerRadius = t.Metrics.OverlayCornerRadius;
                menu.BorderWidth = 0f;
                menu.BoxShadow = BoxShadow.Large;
            })

            .For<FlyoutPresenter>((presenter, t) =>
            {
                presenter.Background = t.Colors.Surface;
                presenter.BorderWidth = 0f;
                presenter.CornerRadius = t.Metrics.OverlayCornerRadius;
                presenter.BoxShadow = BoxShadow.Large;
            })

            // the navigation bar of iOS: on the grouped background, a hairline under it
            .For<AppBar>((bar, c) =>
            {
                bar.Background = c.Background;
                bar.DividerColor = c.Border;
            })

            .For<AppBarButton>((button, c) =>
            {
                button.BackgroundColor = Colors.Transparent;
                button.HoverBackgroundColor = Colors.Transparent;
                button.PressedBackgroundColor = Colors.Transparent;
                button.TextColor = c.Accent;
                button.PressedTextColor = c.Accent.WithA(0x80);
                button.DisabledTextColor = c.TextDisabled;
                button.BorderWidth = 0f;
            })

            // the tab bar: the selected tab in the accent, the others grey, no indicator
            .For<NavigationBar>((bar, c) =>
            {
                bar.Background = c.IsDark ? new Color(255, 0x12, 0x12, 0x12) : new Color(255, 0xF9, 0xF9, 0xF9);
                bar.DividerColor = c.Border;
                bar.SelectedColor = c.Accent;
                bar.IndicatorColor = Colors.Transparent;
                bar.ItemColor = new Color(255, 0x99, 0x99, 0x99);
                bar.DisabledColor = c.TextDisabled;
                bar.BadgeColor = c.Error;
                bar.BadgeTextColor = Colors.White;
            })

            // the tabs of a segmented control's colors: grey, the selected one white
            .For<TabStrip>((strip, c) =>
            {
                strip.StripColor = c.Background;
                strip.TabHoverColor = c.SurfaceHover;
                strip.SelectedTabColor = c.Surface;
                strip.DisabledTextColor = c.TextDisabled;
                strip.AccentColor = c.Accent;
            })

            // a sheet rounded 10 at the top, its grabber grey
            .For<BottomSheet>((sheet, t) =>
            {
                ThemeColors c = t.Colors;

                sheet.Background = c.Surface;
                sheet.BoxShadow = BoxShadow.Medium;
                sheet.ScrimColor = c.IsDark ? new Color(110, 0, 0, 0) : new Color(82, 0, 0, 0);
                sheet.HandleColor = c.ControlStrongStroke;
                sheet.CornerRadius = new CornerRadius(10f, 10f, 0f, 0f);
            });
    }
}
