using System.Globalization;
using ZeppelinForms.Core.Collections;
using ZeppelinForms.Core.Globalization;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Layout;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

public partial class ColorPicker : InteractiveControl
{
    private readonly FlyoutHost _flyout;
    private Color _value = Colors.Black;

    /// <summary>Brings the open editor — its sliders and spectrum — to a value set
    /// from elsewhere: by another part of the editor, the eyedropper or the
    /// application. Null while the editor is closed.</summary>
    private Action? _editorSync;

    public Color Value
    {
        get => _value;
        set
        {
            if (_value == value) return;

            _value = value;

            // the editor first: a handler of ValueChanged reading the sliders
            // must find them at the new value
            _editorSync?.Invoke();

            ValueChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
    }

    public event EventHandler? ValueChanged;

    public bool ShowHex { get; set; } = true;

    /// <summary>The editor gets a fourth slider, for the alpha channel; the hex shows
    /// it as #RRGGBBAA — alpha last, as in CSS and in .zss — whenever the color
    /// isn't opaque. Without it the editor leaves the alpha of <see cref="Value"/> as it is.</summary>
    public bool AllowAlpha
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // the hex may grow by two digits
            Invalidate();
        }
    }

    /// <summary>The editor gets a spectrum above its sliders: a field of saturation
    /// and brightness and a strip of hues, picked by the mouse. Off by default.</summary>
    /// <remarks>The sliders stay: they are what the keyboard and the screen reader
    /// work with, and the spectrum follows them as they follow it.</remarks>
    public bool ShowSpectrum { get; set; }

    /// <summary>The editor gets an eyedropper button: the form is captured, and a click
    /// anywhere on it takes the color under the cursor. Off by default.</summary>
    /// <remarks>
    /// <para>
    /// The form only, not the whole screen: what is outside the window belongs to
    /// other applications, and Wayland and the browser give no way to read it.
    /// </para>
    /// <para>
    /// While picking, a magnifier follows the cursor; the arrow keys move the point
    /// by a pixel (with Shift by ten), Enter or a click takes the color, Escape or the
    /// right button cancels. The alpha of <see cref="Value"/> is kept: a form has no
    /// translucent pixels to take one from.
    /// </para>
    /// </remarks>
    public bool ShowEyedropper { get; set; }

    public Color SwatchBorderColor { get; set; } = new Color(255, 140, 140, 140);

    public ColorPicker()
    {
        SetControlDefault(BackgroundProperty, Colors.White);
        SetControlDefault(PaddingProperty, new(4, 3));
        Cursor = CursorKind.Hand;
        SetControlDefault(BorderColorProperty, Colors.Black);
        SetControlDefault(BorderWidthProperty, 1f);

        _flyout = new FlyoutHost(this);
        _flyout.Closed += (_, _) =>
        {
            _editorSync = null;
            InvalidateVisual();
        };
    }

    /// <summary>Whether the drop-down is open — for the accessibility peer,
    /// which reports it as the expanded state.</summary>
    internal bool IsDropDownOpen => _flyout.IsOpen;

    private string HexOf(Color c) => AllowAlpha && c.A != 255
        ? string.Create(CultureInfo.InvariantCulture, $"#{c.R:X2}{c.G:X2}{c.B:X2}{c.A:X2}")
        : string.Create(CultureInfo.InvariantCulture, $"#{c.R:X2}{c.G:X2}{c.B:X2}");

    /// <summary>The hex code the picker shows — and the accessibility peer reads.</summary>
    internal string HexText => HexOf(_value);

    /// <summary>The checkerboard under a translucent color, as every color editor
    /// shows it: on a plain background a half-transparent red reads as pink.</summary>
    private static void DrawSwatch(Graphics g, Rectangle rect, CornerRadius radius, Color color, Color border)
    {
        if (color.A < 255)
        {
            const float cell = 4f;

            g.Save();
            g.ClipRoundRect(rect, radius);

            g.FillRectangle(rect, Colors.White);

            int columns = (int)MathF.Ceiling(rect.Width / cell);
            int rows = (int)MathF.Ceiling(rect.Height / cell);

            for (int row = 0; row < rows; row++)
                for (int column = (row & 1); column < columns; column += 2)
                    g.FillRectangle(
                        new Rectangle(
                            new Point(rect.X + column * cell, rect.Y + row * cell),
                            new Size(cell, cell)),
                        new Color(255, 204, 204, 204));

            g.Restore();
        }

        g.FillRoundRectangle(rect, radius, color);
        g.DrawRoundRectangle(rect, radius, border, 1f);
    }

    protected override void DrawContent(Graphics g)
    {
        var content = ContentBounds;
        float swatchSize = Math.Max(0, content.Height - 2);

        var swatch = new Rectangle(
            new Point(content.X, content.Y + 1), new Size(swatchSize, swatchSize));

        DrawSwatch(g, swatch, new CornerRadius(2f), _value, SwatchBorderColor);

        if (!ShowHex) return;

        g.DrawTextLine(HexOf(_value),
            new Rectangle(
                new Point(content.X + swatchSize + 6, content.Y),
                new Size(Math.Max(0, content.Width - swatchSize - 6), content.Height)),
            TextColor, EffectiveFont,
            HorizontalContentAlignment.Left, VerticalContentAlignment.Center);
    }

    protected override void OnClick(MouseClickEventArgs e)
    {
        e.Handled = true;
        _flyout.Toggle(BuildEditor);
    }

    /// <summary>Close the editor. FlyoutHost raises no Closed for a close of its own,
    /// so the sync is dropped here as well.</summary>
    private void CloseEditor()
    {
        _flyout.Close();
        _editorSync = null;
    }

    private UIElement BuildEditor()
    {
        // drawn rather than a panel's background: a translucent color needs the
        // checkerboard under it
        var preview = new SwatchView(this)
        {
            Size = new Size(float.NaN, 28),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        // each slider with the channel it shows: the editor sync puts them back
        // when the value comes from elsewhere
        var sliders = new List<(TrackBar Slider, Func<Color, byte> Channel)>();

        TrackBar MakeChannel(Func<Color, byte> channel, Action<byte> apply)
        {
            var slider = new TrackBar
            {
                Minimum = 0,
                Maximum = 255,
                Step = 1,
                Value = channel(_value),
                Size = new Size(180, 24),
            };

            slider.ValueChanged += (_, _) =>
            {
                // a slider put back by the sync applies the channel the value
                // already has — Value ignores an equal color, nothing loops
                apply((byte)slider.Value);

                preview.InvalidateVisual();
            };

            sliders.Add((slider, channel));

            return slider;
        }

        Label Caption(string text) => new()
        {
            Text = text,
            TextColor = App.Theme.Colors.TextSecondary,
            HorizontalContentAlign = HorizontalContentAlignment.Left,
        };

        var channels = new StackPanel
        {
            Spacing = 4,
            Padding = new Thickness(8),
        };

        SpectrumView? spectrum = null;

        if (ShowSpectrum)
        {
            spectrum = new SpectrumView(this)
            {
                Size = new Size(180, 150),
            };

            channels.Children.Add(spectrum);
        }

        if (ShowEyedropper)
        {
            var eyedropper = new Button
            {
                Icon = IconSource.FromPath(EyedropperIcon),
                IconSize = 16,
                Size = new Size(28, 28),
                Padding = new Thickness(0),
                Margin = new Thickness(6, 0, 0, 0),
                Docking = Dock.Right,
            };

            eyedropper
                .Localize(nameof(ToolTip), static (b, text) => b.ToolTip = text, ZfText.PickColor)
                .Localize(nameof(AccessibleName), static (b, text) => b.AccessibleName = text, ZfText.PickColor);

            eyedropper.Click += (_, _) => StartPicking();

            // the button docked first, the preview fills the rest as the last child
            channels.Children.Add(new DockPanel
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Children = { eyedropper, preview },
            });
        }
        else
        {
            channels.Children.Add(preview);
        }

        channels.Children.Add(Caption("R"));
        channels.Children.Add(MakeChannel(static c => c.R, v => Value = new Color(_value.A, v, _value.G, _value.B)));
        channels.Children.Add(Caption("G"));
        channels.Children.Add(MakeChannel(static c => c.G, v => Value = new Color(_value.A, _value.R, v, _value.B)));
        channels.Children.Add(Caption("B"));
        channels.Children.Add(MakeChannel(static c => c.B, v => Value = new Color(_value.A, _value.R, _value.G, v)));

        if (AllowAlpha)
        {
            channels.Children.Add(Caption("A"));
            channels.Children.Add(MakeChannel(static c => c.A, v => Value = new Color(v, _value.R, _value.G, _value.B)));
        }

        _editorSync = () =>
        {
            foreach ((TrackBar slider, Func<Color, byte> channel) in sliders)
                slider.Value = channel(_value);

            spectrum?.Sync();
            preview.InvalidateVisual();
        };

        return new Border
        {
            Background = App.Theme.Colors.Surface,
            BorderColor = App.Theme.Colors.Border,
            BorderWidth = 1,
            CornerRadius = new CornerRadius(4f),
            Child = channels,
        };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _flyout.IsOpen)
        {
            CloseEditor();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Size probe = TextMeasurer.MeasureLine(AllowAlpha ? "#FFFFFFFF" : "#FFFFFF", EffectiveFont);

        float width = ShowHex
            ? probe.Height + 6 + probe.Width + Padding.Horizontal + 4
            : probe.Height + Padding.Horizontal;

        return ResolveSize(new Size(width, probe.Height + Padding.Vertical + 6), availableSize);
    }

    /// <summary>The editor's preview: the picker's current color over the checkerboard.</summary>
    private sealed class SwatchView(ColorPicker owner) : DecoratedControl
    {
        protected override void DrawContent(Graphics g) =>
            DrawSwatch(g, ContentBounds, new CornerRadius(2f), owner.Value, owner.SwatchBorderColor);
    }
}