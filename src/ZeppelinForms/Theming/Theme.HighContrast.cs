using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.DataGrid;

namespace ZeppelinForms.Theming;

/// <summary>The high contrast theme, built from the system's own colors.</summary>
/// <remarks>
/// <para>
/// The rules of contrast themes everywhere: only the palette's colors, and each
/// text color only on the background it was paired with — WindowText on Window,
/// ButtonText on ButtonFace, HighlightText on Highlight. States are told by
/// outlines and by the Highlight pair rather than by shades: hover and press
/// change a border or swap to the selection pair, never to a lighter fill no one
/// with low vision can see. Every stroke is solid and at least a pixel wide, the
/// focus ring two.
/// </para>
/// <para>
/// Built on the shared rules like the other themes, so a control without a rule of
/// its own here still takes its colors from the contrast palette.
/// </para>
/// </remarks>
public static partial class Themes
{
    /// <summary>The built-in contrast theme in the classic black palette —
    /// for an application that offers contrast as its own choice.</summary>
    public static Theme HighContrastBlack { get; } = HighContrast(HighContrastPalette.Black);

    /// <summary>The built-in contrast theme in the classic white palette.</summary>
    public static Theme HighContrastWhite { get; } = HighContrast(HighContrastPalette.White);

    /// <summary>A contrast theme in the given colors — the system's, as App does
    /// while the system's high contrast is on, or the application's own.</summary>
    public static Theme HighContrast(HighContrastPalette p)
    {
        var colors = new ThemeColors
        {
            Background = p.Window,
            Surface = p.Window,

            // no lighter shade for hover and press: they are told by outlines
            SurfaceHover = p.Window,
            SurfacePressed = p.Window,

            Border = p.WindowText,
            BorderFocused = p.Highlight,

            Text = p.WindowText,
            TextSecondary = p.WindowText,
            TextDisabled = p.GrayText,
            TextOnAccent = p.HighlightText,

            Accent = p.Highlight,
            AccentHover = p.Highlight,
            AccentPressed = p.Highlight,

            Selection = p.Highlight,
            TextSelection = p.HighlightText,

            // contrast has no red and no green: the hotlight color at least
            // tells an error from the ordinary border, the text says the rest
            Success = p.WindowText,
            Warning = p.Hotlight,
            Error = p.Hotlight,

            ScrollTrack = p.Window,
            ScrollThumb = p.WindowText,

            ControlFill = p.ButtonFace,
            ControlStroke = p.ButtonText,
            ControlStrokeSecondary = p.ButtonText,
            ControlStrongStroke = p.ButtonText,
            FocusStrokeOuter = p.WindowText,
            FocusStrokeInner = p.Window,
        };

        ThemeMetrics metrics = ThemeMetrics.Default with
        {
            FocusStrokeThickness = 2f,
            FocusStrokeInnerThickness = 0f,
        };

        return Build(colors, "HighContrast", metrics, accentRule: KeepContrastColors)

            // every control: a solid outline that turns Highlight under the pointer
            // and with the focus, and a two-pixel focus ring in the text color
            .For<InteractiveControl>((control, t) =>
            {
                ThemeColors c = t.Colors;

                control.BorderColor = c.Border;
                control.HoverBorderColor = c.Accent;
                control.FocusBorderColor = c.Accent;
                control.FocusRingColor = c.FocusStrokeOuter;
                control.FocusRingInnerColor = Colors.Transparent;
                control.FocusRingThickness = t.Metrics.FocusStrokeThickness;
                control.FocusRingInnerThickness = 0f;
            })

            // all buttons alike: contrast has no accent or danger buttons, and a
            // colored fill would put the caption on a background not paired with it.
            // A press changes nothing but the outline; a latched toggle swaps to
            // the selection pair, caption included
            .For<ButtonBase>((button, c) => ContrastButton(button, c, p))
            .For<PrimaryButton>((button, c) => ContrastButton(button, c, p))
            .For<SecondaryButton>((button, c) => ContrastButton(button, c, p))
            .For<DangerButton>((button, c) => ContrastButton(button, c, p))

            .For<ToggleButton>((button, c) =>
            {
                ContrastButton(button, c, p);
                button.CheckedTextColor = p.HighlightText;
            })

            .For<CheckBox>((box, c) =>
            {
                box.BoxBackground = c.Background;
                box.BoxBorderColor = c.Text;
                box.CheckColor = c.Accent;
                box.CheckGlyphColor = c.TextOnAccent;
            })

            .For<RadioButton>((radio, c) =>
            {
                radio.CircleBackground = c.Background;
                radio.CircleBorderColor = c.Text;
                radio.CheckColor = c.Accent;
            })

            // the track outlined in the text color: an empty track filled with the
            // page's own color would vanish
            .For<ToggleSwitch>((toggle, c) =>
            {
                toggle.OnColor = c.Accent;
                toggle.OffColor = c.Background;
                toggle.TrackBorderWidth = 1f;
                toggle.OffBorderColor = c.Text;
                toggle.ThumbColor = c.Text;
                toggle.OnThumbColor = c.TextOnAccent;
            })

            .For<TrackBar>((bar, c) =>
            {
                bar.TrackColor = c.Text;
                bar.FillColor = c.Accent;
                bar.ThumbColor = c.Background;
                bar.ThumbBorderColor = c.Text;
            })

            .For<ProgressBar>((bar, c) =>
            {
                bar.FillColor = c.Accent;
                bar.TrackColor = c.Background;
                bar.BorderColor = c.Text;
                bar.BorderWidth = 1f;
            })

            // selected rows, items and days: the selection pair, text included
            .For<ListBox>((list, c) =>
            {
                list.Background = c.Background;
                list.SelectionColor = c.Selection;
                list.SelectedTextColor = c.TextSelection;
                list.HoverColor = Colors.Transparent;
                list.BorderColor = c.Border;
                list.BorderWidth = 1f;
            })

            .For<DataGridView>((grid, c) =>
            {
                grid.Background = c.Background;
                grid.HeaderColor = c.Background;
                grid.HeaderHoverColor = c.Background;
                grid.HeaderTextColor = c.Text;
                grid.SelectionColor = c.Selection;
                grid.SelectedTextColor = c.TextSelection;
                grid.RowHoverColor = Colors.Transparent;
                grid.GridLineColor = c.Border;
            })

            .For<MenuBar>((menu, c) =>
            {
                menu.Background = c.Background;
                menu.HoverColor = c.Selection;
                menu.OpenColor = c.Selection;
                menu.HighlightedTextColor = c.TextSelection;
            })

            .For<MenuList>((menu, c) =>
            {
                menu.Background = c.Background;
                menu.BorderColor = c.Border;
                menu.BorderWidth = 1f;
                menu.DisabledColor = c.TextDisabled;
                menu.HoverColor = c.Selection;
                menu.HighlightedTextColor = c.TextSelection;
                menu.SeparatorColor = c.Border;
            })

            .For<Calendar>((calendar, c) =>
            {
                calendar.Background = c.Background;
                calendar.MutedColor = c.TextDisabled;
                calendar.SelectionColor = c.Selection;
                calendar.SelectedTextColor = c.TextSelection;
                calendar.TodayColor = c.Background;
                calendar.HoverColor = c.Background;
                calendar.HeaderHoverColor = c.Background;
            })

            .For<TabControl>((tabs, c) =>
            {
                tabs.HeaderColor = c.Background;
                tabs.HeaderHoverColor = c.Background;
                tabs.SelectedHeaderColor = c.Background;
                tabs.DisabledTextColor = c.TextDisabled;
                tabs.AccentColor = c.Accent;
            });
    }

    private static void ContrastButton(ButtonBase button, ThemeColors c, HighContrastPalette p)
    {
        button.BackgroundColor = p.ButtonFace;
        button.HoverBackgroundColor = p.ButtonFace;
        button.PressedBackgroundColor = p.ButtonFace;
        button.CheckedBackgroundColor = p.Highlight;
        button.CheckedHoverBackgroundColor = p.Highlight;
        button.CheckedPressedBackgroundColor = p.Highlight;
        button.DisabledBackgroundColor = p.ButtonFace;

        button.TextColor = p.ButtonText;
        button.DisabledTextColor = p.GrayText;
        button.BorderColor = p.ButtonText;
        button.BorderWidth = 1f;
        button.ElevationBorderColor = Colors.Transparent;
        button.RippleEnabled = false;
    }

    /// <summary>A contrast theme takes no accent: its colors are the user's,
    /// chosen in the system for exactly this purpose.</summary>
    private static ThemeColors KeepContrastColors(ThemeColors colors, AccentPalette accent) => colors;
}