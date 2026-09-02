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

public class ColorPicker : UnitControl, IInputElement, IBorderedElement
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

    public Color TextColor { get; set; } = Colors.Black;
    public Color BorderColor { get; set; } = Colors.Black;
    public float BorderWidth { get; set; } = 1f;

    public bool IsFocused { get; set; }
    public bool TabStop { get; set; } = true;
    public uint TabIndex { get; set; }

    protected override bool IsKeyActivatable => true;

    public ColorPicker()
    {
        Background = Colors.White;
        Padding = new Thickness(4, 3);
        Cursor = CursorKind.Hand;

        _flyout = new FlyoutHost(this);
        _flyout.Closed += (_, _) => InvalidateVisual();
    }

    private static string HexOf(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public override void Draw(Graphics g)
    {
        if (Background.A > 0)
            g.FillRoundRectangle(this.LocalBounds, CornerRadius, Background);

        if (BorderWidth > 0)
            g.DrawRoundRectangle(this.LocalBounds, CornerRadius,
                IsFocused ? App.Theme.Colors.BorderFocused : BorderColor, BorderWidth);

        var content = this.ContentBounds;
        float swatchSize = Math.Max(0, content.Height - 2);

        var swatch = new Rectangle(
            new Point(content.X, content.Y + 1), new Size(swatchSize, swatchSize));

        g.FillRoundRectangle(swatch, new CornerRadius(2f), _value);
        g.DrawRoundRectangle(swatch, new CornerRadius(2f), new Color(255, 140, 140, 140), 1f);

        if (!ShowHex) return;

        var textArea = new Rectangle(
            new Point(content.X + swatchSize + 6, content.Y),
            new Size(Math.Max(0, content.Width - swatchSize - 6), content.Height));

        g.DrawText(HexOf(_value), textArea, TextColor, EffectiveFont,
            HorizontalContentAlignment.Left, VerticalContentAlignment.Center);
    }

    protected override void OnClick(MouseClickEventArgs e)
    {
        e.Handled = true;
        _flyout.Toggle(BuildEditor);
    }

    private UIElement BuildEditor()
    {
        var preview = new Panel
        {
            Size = new Size(float.NaN, 28),
            Background = _value,
            BorderColor = new Color(255, 140, 140, 140),
            BorderWidth = 1,
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
                preview.Background = _value;
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

        var stack = new StackPanel
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

        return new Border
        {
            Background = App.Theme.Colors.Surface,
            BorderColor = App.Theme.Colors.Border,
            BorderWidth = 1,
            CornerRadius = new CornerRadius(4f),
            Child = stack,
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
        Size probe = TextMeasurer.Current.MeasureText("#FFFFFF", EffectiveFont);

        float width = ShowHex
            ? probe.Height + 6 + probe.Width + Padding.Horizontal + 4
            : probe.Height + Padding.Horizontal;

        return ResolveSize(new Size(width, probe.Height + Padding.Vertical + 6), availableSize);
    }
}