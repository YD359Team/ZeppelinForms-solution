using System.Globalization;
using ZeppelinForms.Core.Globalization;
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

    /// <summary>The date format. Null — the culture's short date pattern.</summary>
    /// <remarks>Used to default to "dd.MM.yyyy" — the Russian format for everyone.</remarks>
    public string? Format
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // the picker's size is computed from the formatted date
            Invalidate();
        }
    }

    /// <summary>The culture for the date and for the drop-down calendar.
    /// Null — the interface language, Localization.Culture.</summary>
    public CultureInfo? Culture
    {
        get;
        set
        {
            if (ReferenceEquals(field, value)) return;

            field = value;
            Invalidate();
        }
    }

    private CultureInfo EffectiveCulture => Culture ?? Localization.Culture;

    /// <summary>The value as the picker shows it — also what a screen reader reads.</summary>
    internal string FormattedValue => Value.ToString(Format ?? "d", EffectiveCulture);

    public event EventHandler? ValueChanged;

    public bool IsDropDownOpen => _flyout.IsOpen;

    public DateTimePicker()
    {
        SetControlDefault(BackgroundProperty, Colors.White);
        SetControlDefault(PaddingProperty, new(6, 3));
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

        g.DrawText(FormattedValue,
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

        // the "header" with the date — a fill of the top strip
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


    /// <summary>The calendar of the open drop-down; null while it is closed.</summary>
    private Calendar? _calendar;

    private UIElement BuildCalendar()
    {
        var calendar = new Calendar();
        _calendar = calendar;
        // the calendar follows the picker: a picker with a culture of its own
        // must not open a calendar in the interface language
        calendar.Culture = Culture;
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
        // while the drop-down is open the calendar has the arrows: the focus stays
        // on the picker, and the calendar inside the flyout can't take it
        if (_flyout.IsOpen && _calendar?.HandleNavigationKey(e.Key, e.Modifiers) == true)
        {
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Escape when _flyout.IsOpen:
                _flyout.Close();
                e.Handled = true;
                break;

            // the drop-down from the keyboard: Alt+Down and F4, as every combo box
            case Key.Down when e.Modifiers.HasFlag(KeyModifiers.Alt):
            case Key.F4:
                _flyout.Toggle(BuildCalendar);
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
        Size textSize = TextMeasurer.Current.MeasureText(FormattedValue, EffectiveFont);

        return ResolveSize(
            new Size(
                textSize.Width + IconWidth + Padding.Horizontal,
                textSize.Height + Padding.Vertical + 6),
            availableSize);
    }
}