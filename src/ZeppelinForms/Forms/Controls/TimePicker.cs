using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Layout;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

public class TimePicker : InteractiveControl
{
    private const float IconWidth = 18f;

    private readonly FlyoutHost _flyout;

    public TimeOnly Value { get; private set; } = new(12, 0);
    public string Format { get; set; } = "HH:mm";
    public int MinuteStep { get; set; } = 5;

    public event EventHandler? ValueChanged;

    public bool IsDropDownOpen => _flyout.IsOpen;

    public TimePicker()
    {
        Background = Colors.White;
        Padding = new Thickness(6, 3);
        Cursor = CursorKind.Hand;
        BorderColor = Colors.Black;
        BorderWidth = 1f;

        _flyout = new FlyoutHost(this);
        _flyout.Closed += (_, _) => InvalidateVisual();
    }

    public void SetValue(TimeOnly value)
    {
        if (Value == value) return;

        Value = value;
        ValueChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    protected override void DrawContent(Graphics g)
    {
        var content = ContentBounds;

        g.DrawText(Value.ToString(Format),
            new Rectangle(content.Position, new Size(Math.Max(0, content.Width - IconWidth), content.Height)),
            TextColor, EffectiveFont,
            HorizontalContentAlignment.Left, VerticalContentAlignment.Center);

        DrawClockIcon(g,
            new Rectangle(
                new Point(content.X + content.Width - IconWidth, content.Y),
                new Size(IconWidth, content.Height)),
            TextColor);
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
        e.Handled = true;
        _flyout.Toggle(BuildPicker);
    }

    private UIElement BuildPicker()
    {
        var hours = new ListBox
        {
            Size = new Size(64, float.NaN),
            OverflowY = Overflow.Auto,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        for (int h = 0; h < 24; h++)
            hours.Items.Add(h.ToString("00"));

        hours.SelectedIndex = Value.Hour;

        var minutes = new ListBox
        {
            Size = new Size(64, float.NaN),
            OverflowY = Overflow.Auto,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        int step = Math.Max(1, MinuteStep);

        for (int m = 0; m < 60; m += step)
            minutes.Items.Add(m.ToString("00"));

        minutes.SelectedIndex = Math.Min(Value.Minute / step, minutes.Items.Count - 1);

        void Apply()
        {
            if (hours.SelectedItem is string h && minutes.SelectedItem is string m)
                SetValue(new TimeOnly(int.Parse(h), int.Parse(m)));
        }

        hours.SelectionChanged += (_, _) => Apply();
        minutes.SelectionChanged += (_, _) => Apply();

        return new Border
        {
            Background = App.Theme.Colors.Surface,
            BorderColor = App.Theme.Colors.Border,
            BorderWidth = 1,
            CornerRadius = new CornerRadius(4f),
            Padding = new Thickness(4),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Size = new Size(float.NaN, 180),
                Children = { hours, minutes },
            },
        };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape when _flyout.IsOpen:
                _flyout.Close();
                e.Handled = true;
                break;

            case Key.Up:
                SetValue(Value.AddMinutes(MinuteStep));
                e.Handled = true;
                break;

            case Key.Down:
                SetValue(Value.AddMinutes(-MinuteStep));
                e.Handled = true;
                break;

            default:
                base.OnKeyDown(e);
                break;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Size textSize = TextMeasurer.Current.MeasureText(Value.ToString(Format), EffectiveFont);

        return ResolveSize(
            new Size(
                textSize.Width + IconWidth + Padding.Horizontal,
                textSize.Height + Padding.Vertical + 6),
            availableSize);
    }
}