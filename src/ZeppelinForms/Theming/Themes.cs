using ZeppelinForms.Drawing.Helpers;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Navigation;
using ZeppelinForms.Forms.Controls.Shapes;
using ZeppelinForms.Forms.Controls.Text;

namespace ZeppelinForms.Theming;

public static class Themes
{
    public static Theme Light { get; } = Build(new ThemeColors
    {
        Background = new Color(255, 0xFA, 0xFA, 0xFA),
        Surface = Colors.White,
        SurfaceHover = new Color(255, 0xF0, 0xF0, 0xF0),
        SurfacePressed = new Color(255, 0xE4, 0xE4, 0xE4),

        Border = new Color(255, 0xCE, 0xCE, 0xCE),
        BorderFocused = new Color(255, 0x0D, 0x6E, 0xFD),

        Text = new Color(255, 0x21, 0x21, 0x21),
        TextSecondary = new Color(255, 0x6C, 0x75, 0x7D),
        TextDisabled = new Color(255, 0xA8, 0xA8, 0xA8),
        TextOnAccent = Colors.White,

        Accent = new Color(255, 0x0D, 0x6E, 0xFD),
        AccentHover = new Color(255, 0x0B, 0x5E, 0xD7),
        AccentPressed = new Color(255, 0x0A, 0x53, 0xBE),

        Selection = new Color(255, 0xAD, 0xD6, 0xFF),
        TextSelection = new Color(255, 0x21, 0x21, 0x21),

        Success = new Color(255, 0x19, 0x87, 0x54),
        Warning = new Color(255, 0xFF, 0xC1, 0x07),
        Error = new Color(255, 0xDC, 0x35, 0x45),

        ScrollTrack = new Color(40, 0, 0, 0),
        ScrollThumb = new Color(120, 0, 0, 0),
    }, "Light");

    public static Theme Dark { get; } = Build(new ThemeColors
    {
        Background = new Color(255, 0x1E, 0x1E, 0x1E),
        Surface = new Color(255, 0x2D, 0x2D, 0x30),
        SurfaceHover = new Color(255, 0x3E, 0x3E, 0x42),
        SurfacePressed = new Color(255, 0x50, 0x50, 0x55),

        Border = new Color(255, 0x3F, 0x3F, 0x46),
        BorderFocused = new Color(255, 0x3B, 0x8E, 0xEA),

        Text = new Color(255, 0xE8, 0xE8, 0xE8),
        TextSecondary = new Color(255, 0x9C, 0x9C, 0x9C),
        TextDisabled = new Color(255, 0x66, 0x66, 0x66),
        TextOnAccent = Colors.White,

        Accent = new Color(255, 0x3B, 0x8E, 0xEA),
        AccentHover = new Color(255, 0x4D, 0x9D, 0xF5),
        AccentPressed = new Color(255, 0x2C, 0x7A, 0xD0),

        Selection = new Color(255, 0x26, 0x4F, 0x78),
        TextSelection = new Color(255, 0xE8, 0xE8, 0xE8),

        Success = new Color(255, 0x4E, 0xC9, 0x5F),
        Warning = new Color(255, 0xE5, 0xB5, 0x3D),
        Error = new Color(255, 0xF1, 0x4C, 0x4C),

        ScrollTrack = new Color(48, 255, 255, 255),
        ScrollThumb = new Color(110, 255, 255, 255),
    }, "Dark");

    private static Theme Build(ThemeColors colors, string name)
    {
        return new Theme { Name = name, Colors = colors }

            .For<InteractiveControl>((control, c) =>
            {
                control.BorderColor = c.Border;
                control.FocusBorderColor = c.BorderFocused;
            })

            // общее для всех — фон окна и цвет полос прокрутки
            .For<DecoratedPanel>((panel, c) =>
            {
                panel.ScrollTrackColor = c.ScrollTrack;
                panel.ScrollThumbColor = c.ScrollThumb;
            })

            .For<Label>((label, c) => label.TextColor = c.Text)

            .For<RichLabel>((label, c) => label.TextColor = c.Text)

            // база — нейтральная кнопка, если тип не уточнён
            .For<ButtonBase>((button, c) =>
            {
                button.BackgroundColor = c.Surface;
                button.HoverBackgroundColor = c.SurfaceHover;
                button.PressedBackgroundColor = c.SurfacePressed;
                button.CheckedBackgroundColor = c.Accent;
                button.DisabledBackgroundColor = c.SurfacePressed;
                button.TextColor = c.Text;
                button.DisabledTextColor = c.TextDisabled;
                button.FocusRingColor = c.Accent;
                button.RippleColor = new Color(70, 255, 255, 255);
            })

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
                button.FocusRingColor = c.TextOnAccent;
            })

            .For<SecondaryButton>((button, c) =>
            {
                // контурная: заливки нет, цвет берёт акцент
                button.BackgroundColor = Colors.Transparent;
                button.HoverBackgroundColor = c.SurfaceHover;
                button.PressedBackgroundColor = c.SurfacePressed;
                button.CheckedBackgroundColor = c.Accent;
                button.DisabledBackgroundColor = Colors.Transparent;
                button.TextColor = c.Accent;
                button.DisabledTextColor = c.TextDisabled;
                button.BorderColor = c.Accent;
                button.FocusRingColor = c.Accent;
                button.RippleColor = new Color(40, 0, 0, 0);
            })

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
                button.FocusRingColor = c.TextOnAccent;
            })

            .For<ToggleButton>((button, c) =>
            {
                button.BackgroundColor = c.Surface;
                button.HoverBackgroundColor = c.SurfaceHover;
                button.PressedBackgroundColor = c.SurfacePressed;

                button.CheckedBackgroundColor = c.Accent;
                button.CheckedHoverBackgroundColor = c.AccentHover;
                button.CheckedPressedBackgroundColor = c.AccentPressed;

                button.DisabledBackgroundColor = c.SurfacePressed;
                button.TextColor = c.Text;
                button.CheckedTextColor = c.TextOnAccent;
                button.DisabledTextColor = c.TextDisabled;
                button.FocusRingColor = c.Accent;
            })

            .For<CheckBox>((box, c) =>
            {
                box.TextColor = c.Text;
                box.BoxBackground = c.Surface;
                box.BoxBorderColor = c.Border;
                box.CheckColor = c.Accent;
            })

            .For<RadioButton>((radio, c) =>
            {
                radio.TextColor = c.Text;
                radio.CircleBorderColor = c.Border;
                radio.CheckColor = c.Accent;
            })

            .For<TextBox>((box, c) =>
            {
                box.Background = c.Surface;
                box.TextColor = c.Text;
                box.CaretColor = c.Text;
                box.SelectionColor = c.Selection;
            })

            .For<ComboBox>((combo, c) =>
            {
                combo.Background = c.Surface;
                combo.TextColor = c.Text;
                combo.PlaceholderColor = c.TextSecondary;
            })

            .For<ListBox>((list, c) =>
            {
                list.Background = c.Surface;
                list.SelectionColor = c.Accent;
                list.BorderColor = c.Border;
                list.BorderWidth = 1f;
            })

            .For<Panel>((panel, c) =>
            {
                panel.Background = c.Background;
            })

            .For<Border>((border, c) =>
            {
                border.Background = c.Surface;
            })

            .For<GroupBox>((box, c) =>
            {
                box.Background = Colors.Transparent;
                box.HeaderColor = c.TextSecondary;
            })

            .For<Spoiler>((spoiler, c) =>
            {
                spoiler.HeaderColor = c.Surface;
                spoiler.HeaderHoverColor = c.SurfaceHover;
                spoiler.HeaderTextColor = c.Text;
            })

            .For<TabControl>((tabs, c) =>
            {
                tabs.HeaderColor = c.Surface;
                tabs.HeaderHoverColor = c.SurfaceHover;
                tabs.SelectedHeaderColor = c.Background;
                tabs.TextColor = c.Text;
                tabs.DisabledTextColor = c.TextDisabled;
                tabs.AccentColor = c.Accent;
            })

            .For<MenuBar>((menu, c) =>
            {
                menu.Background = c.Surface;
                menu.TextColor = c.Text;
                menu.HoverColor = c.SurfaceHover;
                menu.OpenColor = c.SurfacePressed;
            })

            .For<MenuList>((menu, c) =>
            {
                menu.Background = c.Surface;
                menu.TextColor = c.Text;
                menu.DisabledColor = c.TextDisabled;
                menu.HoverColor = c.SurfaceHover;
                menu.SeparatorColor = c.Border;
            })

            .For<NumericUpDown>((numeric, c) =>
            {
                numeric.Background = c.Surface;
                numeric.TextColor = c.Text;
                numeric.ButtonColor = c.SurfaceHover;
                numeric.ButtonHoverColor = c.SurfacePressed;
            })

            .For<ProgressBar>((bar, c) =>
            {
                bar.FillColor = c.Accent;
                bar.TrackColor = c.SurfaceHover;
                bar.TextColor = c.Text;
            })

            .For<Loader>((loader, c) =>
            {
                loader.Color = c.Accent;
                loader.TrackColor = c.Border;
            })

            .For<TrackBar>((bar, c) =>
            {
                bar.TrackColor = c.SurfaceHover;
                bar.FillColor = c.Accent;
                bar.ThumbColor = c.Surface;
                bar.ThumbBorderColor = c.Border;
            })

            .For<ToggleSwitch>((toggle, c) =>
            {
                toggle.TextColor = c.Text;
                toggle.OnColor = c.Accent;
                toggle.OffColor = c.SurfacePressed;
                toggle.ThumbColor = c.Surface;
            })

            .For<Calendar>((calendar, c) =>
            {
                calendar.Background = c.Surface;
                calendar.TextColor = c.Text;
                calendar.MutedColor = c.TextSecondary;
                calendar.SelectionColor = c.Accent;
                calendar.TodayColor = c.SurfaceHover;
                calendar.HoverColor = c.SurfaceHover;
                calendar.HeaderHoverColor = c.SurfacePressed;
            })

            .For<Shape>((shape, c) =>
            {
                if (shape.Stroke.A == 0) shape.Stroke = c.Border;
            })

            .For<SplitContainer>((split, c) =>
            {
                split.SplitterColor = c.Border;
                split.SplitterHoverColor = c.SurfaceHover;
            })

            .For<GridSplitter>((splitter, c) =>
            {
                splitter.LineColor = c.Border;
                splitter.HoverColor = c.SurfaceHover;
            })

            .For<PageIndicator>((indicator, c) =>
            {
                indicator.ActiveColor = c.Accent;
                indicator.InactiveColor = c.Border;
                indicator.HoverColor = c.TextSecondary;
            })

            .For<DragList>((list, c) =>
            {
                list.Background = c.Surface;
                list.BorderColor = c.Border;
                list.BorderWidth = 1f;
                list.DropIndicatorColor = c.Accent;
                list.DragPreviewBackground = c.Surface;
            });
    }
}
