using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Navigation;
using ZeppelinForms.Forms.Controls.Text;

namespace ZeppelinForms.Theming;

/// <summary>The Material 3 themes: Material You's scheme from a seed color, its shapes and
/// its type scale.</summary>
/// <remarks>
/// <para>
/// The palette is made from a seed (<see cref="MaterialScheme"/>), not written out: the
/// baseline themes from Material's own purple, and <see cref="Theme.WithAccent(Color)"/>
/// makes the whole scheme again from another seed, the surfaces tinted by it as well as
/// the accent. Given the system accent through <see cref="App.UseSystemTheme(Theme, Theme, bool)"/>,
/// that is the dynamic color of Android 12 and later.
/// </para>
/// <para>
/// Material's state layers — the content color laid over a container at 8 % on hover and
/// 10 % on press — are worked out in advance into opaque colors, as the Fluent themes do
/// with WinUI's translucent ones. The roles of the scheme map on the palette's:
/// <list type="table">
/// <item><term>surface</term><description><see cref="ThemeColors.Background"/></description></item>
/// <item><term>surface container</term><description><see cref="ThemeColors.Surface"/> — menus, cards, the navigation bar</description></item>
/// <item><term>surface container highest</term><description><see cref="ThemeColors.ControlFill"/> — a switch's track</description></item>
/// <item><term>secondary container</term><description><see cref="ThemeColors.Selection"/> — tonal buttons, the selected row, the navigation indicator</description></item>
/// <item><term>outline, outline variant</term><description><see cref="ThemeColors.ControlStroke"/>, <see cref="ThemeColors.Border"/></description></item>
/// <item><term>on surface variant</term><description><see cref="ThemeColors.TextSecondary"/>, <see cref="ThemeColors.ControlStrongStroke"/></description></item>
/// </list>
/// </para>
/// <para>
/// The text fields are Material's outlined ones; a neutral <see cref="Button"/> is a tonal
/// button, <see cref="PrimaryButton"/> a filled one, <see cref="SecondaryButton"/> an
/// outlined one; buttons are pills and press with a ripple. The face is Roboto where the
/// system has it.
/// </para>
/// </remarks>
public static partial class Themes
{
    public static Theme MaterialLight { get; } = BuildMaterial(dark: false, "MaterialLight");

    public static Theme MaterialDark { get; } = BuildMaterial(dark: true, "MaterialDark");

    /// <summary>Roboto, Android's face; the system sans-serif where it's missing.</summary>
    private static Font MaterialFont => new("Roboto, Segoe UI, sans-serif", 14);

    /// <summary>How a Material theme takes an accent: the accent is the seed, and the whole
    /// scheme is made again from it — Material You's dynamic color.</summary>
    private static ThemeColors MaterialAccent(ThemeColors colors, AccentPalette palette) =>
        MaterialScheme.Create(palette.Accent, colors.IsDark);

    /// <summary>A pill: wider than any control is tall, and cut to the height by the renderer.</summary>
    private static readonly CornerRadius Pill = new(100f);

    private static Theme BuildMaterial(bool dark, string name)
    {
        ThemeColors colors = MaterialScheme.Create(MaterialScheme.BaselineSeed, dark);

        return Build(colors, name, ThemeMetrics.Material, MaterialFont, MaterialAccent, TypeRamp.Material)

            // outlined: the outline at rest, the content color on hover, the primary with
            // focus; the focus ring is the secondary color, 3 dp, as Material's indicator
            .For<InteractiveControl>((control, t) =>
            {
                ThemeColors c = t.Colors;

                control.BorderColor = c.ControlStroke;
                control.HoverBorderColor = c.Text;
                control.FocusBorderColor = c.Accent;
                control.CornerRadius = t.Metrics.ControlCornerRadius;

                control.FocusRingColor = c.FocusStrokeOuter;
                control.FocusRingThickness = t.Metrics.FocusStrokeThickness;
                control.FocusRingInnerThickness = 0f;
            })

            // a neutral button is a tonal one: the secondary container, no outline, and a
            // ripple of its content color — Material answers a press with the ripple
            .For<ButtonBase>((button, t) =>
            {
                ThemeColors c = t.Colors;

                button.BackgroundColor = c.Selection;
                button.HoverBackgroundColor = MaterialScheme.StateLayer(c.Selection, c.TextSelection, MaterialScheme.Hovered);
                button.PressedBackgroundColor = MaterialScheme.StateLayer(c.Selection, c.TextSelection, MaterialScheme.Pressed);
                button.CheckedBackgroundColor = c.Accent;
                button.DisabledBackgroundColor = MaterialScheme.StateLayer(c.Background, c.Text, 0.12f);
                button.DisabledTextColor = c.TextDisabled;

                button.BorderColor = Colors.Transparent;
                button.ElevationBorderColor = Colors.Transparent;
                button.FocusRingInset = t.Metrics.FocusStrokeThickness / 2f;

                button.RippleEnabled = true;
                button.RippleColor = c.TextSelection.WithA(40);
            })

            // the shape of a button with a label: a pill 40 high, 24 to each side of the
            // label. Not on ButtonBase: the app bar's icon buttons and a split button keep
            // their own
            .For<Button>((button, c) =>
            {
                button.TextColor = c.TextSelection;
                button.CornerRadius = Pill;
                button.Padding = new Thickness(24, 10);
            })

            .For<PrimaryButton>((button, c) =>
            {
                button.BackgroundColor = c.Accent;
                button.HoverBackgroundColor = c.AccentHover;
                button.PressedBackgroundColor = c.AccentPressed;
                button.CheckedBackgroundColor = c.AccentPressed;
                button.TextColor = c.TextOnAccent;
                button.RippleColor = c.TextOnAccent.WithA(50);
            })

            // outlined: the outline, the primary for the label, a state layer of the
            // primary over the page under the pointer
            .For<SecondaryButton>((button, c) =>
            {
                button.BackgroundColor = Colors.Transparent;
                button.HoverBackgroundColor = MaterialScheme.StateLayer(c.Background, c.Accent, MaterialScheme.Hovered);
                button.PressedBackgroundColor = MaterialScheme.StateLayer(c.Background, c.Accent, MaterialScheme.Pressed);
                button.DisabledBackgroundColor = Colors.Transparent;
                button.TextColor = c.Accent;
                button.BorderColor = c.ControlStroke;
                button.RippleColor = c.Accent.WithA(40);
            })

            // a filled button in the error color, its label in the error's own on-color
            .For<DangerButton>((button, c) =>
            {
                Color onError = c.IsDark ? new Color(255, 0x60, 0x14, 0x10) : Colors.White;

                button.BackgroundColor = c.Error;
                button.HoverBackgroundColor = MaterialScheme.StateLayer(c.Error, onError, MaterialScheme.Hovered);
                button.PressedBackgroundColor = MaterialScheme.StateLayer(c.Error, onError, MaterialScheme.Pressed);
                button.CheckedBackgroundColor = MaterialScheme.StateLayer(c.Error, onError, MaterialScheme.Pressed);
                button.TextColor = onError;
                button.RippleColor = onError.WithA(50);
            })

            // tonal while off, filled while on
            .For<ToggleButton>((button, c) =>
            {
                button.CheckedBackgroundColor = c.Accent;
                button.CheckedHoverBackgroundColor = c.AccentHover;
                button.CheckedPressedBackgroundColor = c.AccentPressed;
                button.CheckedTextColor = c.TextOnAccent;
            })

            // outlined text fields over the page; the field draws no underline, and its
            // text keeps the outline at a distance, as Material's 16 dp does at its height
            .For<TextInputControl>((field, c) =>
            {
                field.UnderlineColor = Colors.Transparent;
                field.FocusUnderlineColor = Colors.Transparent;
                field.Padding = new Thickness(12, 8);
            })

            .For<TextBox>((box, c) =>
            {
                box.Background = c.Background;
                box.CaretColor = c.Accent;
                box.SelectionColor = c.Accent.WithA(70);
            })

            .For<MaskedTextBox>((box, c) =>
            {
                box.Background = c.Background;
                box.CaretColor = c.Accent;
            })

            .For<NumericUpDown>((numeric, c) =>
            {
                numeric.Background = c.Background;
                numeric.ButtonColor = Colors.Transparent;
                numeric.ButtonHoverColor = c.SurfaceHover;
                numeric.ArrowColor = c.TextSecondary;
            })

            .For<ComboBox>((combo, c) =>
            {
                combo.Background = c.Background;
                combo.PlaceholderColor = c.TextSecondary;
                combo.Padding = new Thickness(12, 8);
            })

            // 18 dp, rounded 2, a 2 dp outline of the on-surface variant; checked, the box
            // fills with the primary and the mark takes the on-primary
            .For<CheckBox>((box, c) =>
            {
                box.BoxSize = 18f;
                box.BoxCornerRadius = new CornerRadius(2f);
                box.BoxBorderWidth = 2f;
                box.BoxBackground = Colors.Transparent;
                box.BoxBorderColor = c.ControlStrongStroke;
                box.HoverBorderColor = c.Text;
                box.CheckColor = c.Accent;
                box.CheckGlyphColor = c.TextOnAccent;
            })

            // 20 dp, a 2 dp ring; checked, the ring and a 10 dp dot in the primary
            .For<RadioButton>((radio, c) =>
            {
                radio.CircleSize = 20f;
                radio.CircleBorderWidth = 2f;
                radio.DotSize = 10f;
                radio.CircleBackground = Colors.Transparent;
                radio.CircleBorderColor = c.ControlStrongStroke;
                radio.HoverBorderColor = c.Text;
                radio.CheckColor = c.Accent;
                radio.CheckedCircleBackground = Colors.Transparent;
                radio.DotColor = c.Accent;
            })

            // a 52 × 32 track; off — the highest container with the outline and a thumb in
            // it, on — the primary with a thumb of the on-primary
            .For<ToggleSwitch>((toggle, c) =>
            {
                toggle.TrackSize = new Size(52f, 32f);
                toggle.ThumbInset = 6f;
                toggle.TrackBorderWidth = 2f;
                toggle.OnColor = c.Accent;
                toggle.OffColor = c.ControlFill;
                toggle.OffBorderColor = c.ControlStroke;
                toggle.ThumbColor = c.ControlStroke;
                toggle.OnThumbColor = c.TextOnAccent;
            })

            // the inactive track is the secondary container, the thumb the primary itself
            .For<TrackBar>((bar, c) =>
            {
                bar.TrackColor = c.Selection;
                bar.FillColor = c.Accent;
                bar.ThumbColor = c.Accent;
                bar.ThumbBorderColor = Colors.Transparent;
            })

            .For<RangeSlider>((slider, c) =>
            {
                slider.TrackColor = c.Selection;
                slider.FillColor = c.Accent;
                slider.ThumbColor = c.Accent;
                slider.ThumbBorderColor = Colors.Transparent;
            })

            .For<ProgressBar>((bar, c) =>
            {
                bar.FillColor = c.Accent;
                bar.TrackColor = c.Selection;

                // a bar of two fills, without a frame
                bar.BorderWidth = 0f;
                bar.CornerRadius = new CornerRadius(2f);
            })

            // lists without a frame of their own, the selected row in the secondary container
            .For<ListBox>((list, c) =>
            {
                list.Background = c.Background;
                list.SelectionColor = c.Selection;
                list.HoverColor = MaterialScheme.StateLayer(c.Background, c.Text, MaterialScheme.Hovered);
                list.BorderColor = c.Border;
                list.BorderWidth = 1f;
                list.CornerRadius = new CornerRadius(12f);
            })

            // menus float on the surface container, raised by a shadow rather than a line
            .For<MenuList>((menu, t) =>
            {
                ThemeColors c = t.Colors;

                menu.Background = c.Surface;
                menu.DisabledColor = c.TextDisabled;
                menu.HoverColor = c.SurfaceHover;
                menu.SeparatorColor = c.Border;
                menu.CornerRadius = t.Metrics.OverlayCornerRadius;
                menu.BorderWidth = 0f;
                menu.BoxShadow = BoxShadow.Medium;
            })

            .For<FlyoutPresenter>((presenter, t) =>
            {
                presenter.Background = t.Colors.Surface;
                presenter.BorderWidth = 0f;
                presenter.CornerRadius = t.Metrics.OverlayCornerRadius;
                presenter.BoxShadow = BoxShadow.Medium;
            })

            // a plain tooltip: the inverse surface, small and square-cornered
            .For<ToolTip>((tip, t) =>
            {
                ThemeColors c = t.Colors;

                tip.Background = c.Text;
                tip.TextColor = c.Background;
                tip.BorderWidth = 0f;
                tip.CornerRadius = new CornerRadius(4f);
                tip.BoxShadow = null;
            })

            // the top app bar lies on the page itself, without a divider
            .For<AppBar>((bar, c) =>
            {
                bar.Background = c.Background;
                bar.DividerColor = Colors.Transparent;
            })

            // the navigation bar: the surface container, the selected destination in a
            // pill of the secondary container, its icon in the on-secondary-container
            .For<NavigationBar>((bar, c) =>
            {
                bar.Background = c.Surface;
                bar.DividerColor = Colors.Transparent;
                bar.SelectedColor = c.TextSelection;
                bar.IndicatorColor = c.Selection;
                bar.ItemColor = c.TextSecondary;
                bar.DisabledColor = c.TextDisabled;
                bar.BadgeColor = c.Error;
                bar.BadgeTextColor = c.IsDark ? new Color(255, 0x60, 0x14, 0x10) : Colors.White;
            })

            .For<TabStrip>((strip, c) =>
            {
                strip.StripColor = c.Background;
                strip.TabHoverColor = MaterialScheme.StateLayer(c.Background, c.Text, MaterialScheme.Hovered);
                strip.SelectedTabColor = c.Background;
                strip.DisabledTextColor = c.TextDisabled;
                strip.AccentColor = c.Accent;
                strip.ButtonHoverColor = MaterialScheme.StateLayer(c.Background, c.Text, MaterialScheme.Pressed);
            })

            .For<TabControl>((tabs, c) =>
            {
                tabs.HeaderColor = c.Background;
                tabs.HeaderHoverColor = MaterialScheme.StateLayer(c.Background, c.Text, MaterialScheme.Hovered);
                tabs.SelectedHeaderColor = c.Background;
                tabs.DisabledTextColor = c.TextDisabled;
                tabs.AccentColor = c.Accent;
            })

            // the inverse surface, its action in the inverse primary
            .For<Snackbar>((bar, t) =>
            {
                ThemeColors c = t.Colors;

                bar.Background = c.Text;
                bar.MessageColor = c.Background;
                bar.ActionColor = MaterialScheme.Create(c.Accent, !c.IsDark).Accent;
                bar.CornerRadius = new CornerRadius(4f);
                bar.BoxShadow = BoxShadow.Small;
            })

            // a modal sheet is rounded 28 at the top and lies on the low container
            .For<BottomSheet>((sheet, t) =>
            {
                ThemeColors c = t.Colors;

                sheet.Background = c.Surface;
                sheet.BoxShadow = BoxShadow.Medium;
                sheet.ScrimColor = new Color(82, 0, 0, 0);
                sheet.HandleColor = c.TextSecondary.WithA(0x66);
                sheet.CornerRadius = new CornerRadius(28f, 28f, 0f, 0f);
            });
    }
}
