using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Layout;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

public class DateTimePicker : InteractiveControl
{
    private const float IconWidth = 18f;

    private readonly FlyoutHost _flyout;

    public DateTime Value { get; private set; } = DateTime.Today;
    public string Format { get; set; } = "dd.MM.yyyy";

    public event EventHandler? ValueChanged;

    public bool IsDropDownOpen => _flyout.IsOpen;

    public DateTimePicker()
    {
        SetControlDefault(BackgroundProperty, Colors.White);
        Padding = new Thickness(6, 3);
        Cursor = CursorKind.Hand;
        SetControlDefault(BorderColorProperty, Colors.Black);
        SetControlDefault(BorderWidthProperty, 1f);

        _flyout = new FlyoutHost(this);
        _flyout.Closed += (_, _) => InvalidateVisual();
    }

    public void SetValue(DateTime value)
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

        DrawCalendarIcon(g,
            new Rectangle(
                new Point(content.X + content.Width - IconWidth, content.Y),
                new Size(IconWidth, content.Height)),
            TextColor);
    }

    private static void DrawCalendarIcon(Graphics g, Rectangle area, Color color)
    {
        float size = Math.Min(area.Width, area.Height) - 4f;
        if (size <= 0) return;

        float x = area.X + (area.Width - size) / 2f;
        float y = area.Y + (area.Height - size) / 2f;

        var body = new Rectangle(new Point(x, y + size * 0.15f), new Size(size, size * 0.85f));
        g.DrawRectangle(body, color, 1.2f);

        // «шапка» с датой — заливка верхней полосы
        g.FillRectangle(
            new Rectangle(new Point(x, y + size * 0.15f), new Size(size, size * 0.22f)), color);

        g.DrawLine(new Point(x + size * 0.28f, y), new Point(x + size * 0.28f, y + size * 0.2f), color, 1.4f);
        g.DrawLine(new Point(x + size * 0.72f, y), new Point(x + size * 0.72f, y + size * 0.2f), color, 1.4f);

        for (int row = 0; row < 2; row++)
        {
            float ly = y + size * (0.52f + row * 0.22f);
            g.DrawLine(new Point(x + size * 0.18f, ly), new Point(x + size * 0.82f, ly), color, 1f);
        }
    }

    protected override void OnClick(MouseClickEventArgs e)
    {
        e.Handled = true;
        _flyout.Toggle(BuildCalendar);
    }

    private UIElement BuildCalendar()
    {
        var calendar = new Calendar();
        calendar.SetSelectedDate(Value);

        calendar.DateSelected += (_, date) =>
        {
            SetValue(date);
            _flyout.Close();
        };

        return calendar;
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
                SetValue(Value.AddDays(1));
                e.Handled = true;
                break;

            case Key.Down:
                SetValue(Value.AddDays(-1));
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
