using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Layout;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

public class TimePicker : UnitControl, IInputElement, IBorderedElement
{
    private UIElement? _flyout;

    public TimeOnly Value { get; private set; } = new(12, 0);
    public string Format { get; set; } = "HH:mm";
    public int MinuteStep { get; set; } = 5;

    public event EventHandler? ValueChanged;

    public Color TextColor { get; set; } = Colors.Black;
    public Color BorderColor { get; set; } = Colors.Black;
    public float BorderWidth { get; set; } = 1f;

    public bool IsFocused { get; set; }
    public bool TabStop { get; set; } = true;
    public uint TabIndex { get; set; }

    protected override bool IsKeyActivatable => true;

    public TimePicker()
    {
        Background = Colors.White;
        Padding = new Thickness(6, 3);
    }

    public void SetValue(TimeOnly value)
    {
        if (Value == value) return;

        Value = value;
        ValueChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public override void Draw(Graphics g)
    {
        if (Background.A > 0)
            g.FillRectangle(this.LocalBounds, Background);

        if (BorderWidth > 0)
            g.DrawRectangle(this.LocalBounds, IsFocused ? LightThemeColors.ButtonFill : BorderColor, BorderWidth);

        var content = this.ContentBounds;

        g.DrawText(Value.ToString(Format),
            new Rectangle(content.AsPosition(), new Size(Math.Max(0, content.Width - 18), content.Height)),
            TextColor, EffectiveFont,
            HorizontalContentAlignment.Left, VerticalContentAlignment.Center);

        DrawClockIcon(g, new Rectangle(
            new Point(content.X + content.Width - 18, content.Y), new Size(18, content.Height)), TextColor);
    }

    private static void DrawClockIcon(Graphics g, Rectangle area, Color color)
    {
        float size = Math.Min(area.Width, area.Height) - 4f;
        if (size <= 0) return;

        var circle = new Rectangle(
            new Point(area.X + (area.Width - size) / 2f, area.Y + (area.Height - size) / 2f),
            new Size(size, size));

        g.DrawEllipse(circle, color, 1.2f);

        float cx = circle.X + size / 2f;
        float cy = circle.Y + size / 2f;

        // стрелки на 10:10 — так часы рисуют в рекламе, выглядит узнаваемо
        g.DrawLine(new Point(cx, cy), new Point(cx, cy - size * 0.28f), color, 1.2f);
        g.DrawLine(new Point(cx, cy), new Point(cx + size * 0.22f, cy), color, 1.2f);
    }

    protected override void OnClick(MouseClickEventArgs e)
    {
        Form? owner = FindOwner();
        if (owner is null) return;

        e.Handled = true;

        var hours = new ListBox { Size = new Size(60, 160) };
        for (int h = 0; h < 24; h++)
            hours.Items.Add(h.ToString("00"));
        hours.SelectedIndex = Value.Hour;

        var minutes = new ListBox { Size = new Size(60, 160) };
        for (int m = 0; m < 60; m += Math.Max(1, MinuteStep))
            minutes.Items.Add(m.ToString("00"));

        int minuteIndex = Value.Minute / Math.Max(1, MinuteStep);
        minutes.SelectedIndex = Math.Min(minuteIndex, minutes.Items.Count - 1);

        void Apply()
        {
            if (hours.SelectedItem is string h && minutes.SelectedItem is string m)
                SetValue(new TimeOnly(int.Parse(h), int.Parse(m)));
        }

        hours.SelectionChanged += (_, _) => Apply();
        minutes.SelectionChanged += (_, _) => Apply();

        var hoursScroller = new ScrollViewer
        {
            Content = hours,
            Size = new Size(78, 160),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        var minutesScroller = new ScrollViewer
        {
            Content = minutes,
            Size = new Size(78, 160),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        _flyout = new Border
        {
            Background = Colors.White,
            BorderColor = new Color(255, 190, 190, 190),
            BorderWidth = 1,
            Padding = new Thickness(4),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children = { hoursScroller, minutesScroller },
            },
        };

        owner.ShowFlyout(this, _flyout, FlyoutPlacement.Bottom);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Size textSize = TextMeasurer.Current.MeasureText(Value.ToString(Format), EffectiveFont);

        var content = new Size(
            textSize.Width + 18 + Padding.Horizontal,
            textSize.Height + Padding.Vertical + 6);

        return ResolveSize(content, availableSize);
    }
}