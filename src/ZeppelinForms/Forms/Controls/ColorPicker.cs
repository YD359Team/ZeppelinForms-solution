using System.Globalization;
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

public class ColorPicker : InteractiveControl
{
    private readonly FlyoutHost _flyout;
    private Color _value = Colors.Black;

    public Color Value
    {
        get => _value;
        set
        {
            if (_value == value) return;

            _value = value;
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

    public Color SwatchBorderColor { get; set; } = new Color(255, 140, 140, 140);

    public ColorPicker()
    {
        SetControlDefault(BackgroundProperty, Colors.White);
        SetControlDefault(PaddingProperty, new(4, 3));
        Cursor = CursorKind.Hand;
        SetControlDefault(BorderColorProperty, Colors.Black);
        SetControlDefault(BorderWidthProperty, 1f);

        _flyout = new FlyoutHost(this);
        _flyout.Closed += (_, _) => InvalidateVisual();
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

    private UIElement BuildEditor()
    {
        // drawn rather than a panel's background: a translucent color needs the
        // checkerboard under it
        var preview = new SwatchView(this)
        {
            Size = new Size(float.NaN, 28),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        TrackBar MakeChannel(byte initial, Action<byte> apply)
        {
            var slider = new TrackBar
            {
                Minimum = 0,
                Maximum = 255,
                Step = 1,
                Value = initial,
                Size = new Size(180, 24),
            };

            slider.ValueChanged += (_, _) =>
            {
                apply((byte)slider.Value);

                preview.InvalidateVisual();
            };

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
            Children =
            {
                preview,
                Caption("R"),
                MakeChannel(_value.R, v => Value = new Color(_value.A, v, _value.G, _value.B)),
                Caption("G"),
                MakeChannel(_value.G, v => Value = new Color(_value.A, _value.R, v, _value.B)),
                Caption("B"),
                MakeChannel(_value.B, v => Value = new Color(_value.A, _value.R, _value.G, v)),
            },
        };

        if (AllowAlpha)
        {
            channels.Children.Add(Caption("A"));
            channels.Children.Add(MakeChannel(_value.A, v => Value = new Color(v, _value.R, _value.G, _value.B)));
        }

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
            _flyout.Close();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Size probe = TextMeasurer.Current.MeasureText(AllowAlpha ? "#FFFFFFFF" : "#FFFFFF", EffectiveFont);

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