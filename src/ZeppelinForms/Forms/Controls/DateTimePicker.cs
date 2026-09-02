using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Layout;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

public class DateTimePicker : UnitControl, IInputElement, IBorderedElement
{
    private Calendar? _calendar;

    public DateTime Value { get; private set; } = DateTime.Today;
    public string Format { get; set; } = "dd.MM.yyyy";

    public event EventHandler? ValueChanged;

    public Color TextColor { get; set; } = Colors.Black;
    public Color BorderColor { get; set; } = Colors.Black;
    public float BorderWidth { get; set; } = 1f;

    public bool IsFocused { get; set; }
    public bool TabStop { get; set; } = true;
    public uint TabIndex { get; set; }

    public DateTimePicker()
    {
        Background = Colors.White;
        Padding = new Thickness(6, 3);
    }

    public void SetValue(DateTime value)
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
        var textArea = new Rectangle(content.Position, new Size(Math.Max(0, content.Width - 18), content.Height));

        g.DrawText(Value.ToString(Format), textArea, TextColor, EffectiveFont,
            HorizontalContentAlignment.Left, VerticalContentAlignment.Center);

        // стрелка-указатель справа
        var arrowArea = new Rectangle(
            new Point(content.X + content.Width - 18, content.Y), new Size(18, content.Height));

        var iconArea = new Rectangle(
            new Point(content.X + content.Width - 18, content.Y), new Size(18, content.Height));

        DrawCalendarIcon(g, iconArea, TextColor);
    }

    private static void DrawCalendarIcon(Graphics g, Rectangle area, Color color)
    {
        float size = Math.Min(area.Width, area.Height) - 4f;
        if (size <= 0) return;

        float x = area.X + (area.Width - size) / 2f;
        float y = area.Y + (area.Height - size) / 2f;

        // корпус
        var body = new Rectangle(new Point(x, y + size * 0.15f), new Size(size, size * 0.85f));
        g.DrawRectangle(body, color, 1.2f);

        // «шапка» с датой — заливка верхней полосы
        g.FillRectangle(
            new Rectangle(new Point(x, y + size * 0.15f), new Size(size, size * 0.22f)), color);

        // два колечка сверху
        g.DrawLine(new Point(x + size * 0.28f, y), new Point(x + size * 0.28f, y + size * 0.2f), color, 1.4f);
        g.DrawLine(new Point(x + size * 0.72f, y), new Point(x + size * 0.72f, y + size * 0.2f), color, 1.4f);

        // сетка дней — две точки-строки
        for (int row = 0; row < 2; row++)
        {
            float ly = y + size * (0.52f + row * 0.22f);
            g.DrawLine(new Point(x + size * 0.18f, ly), new Point(x + size * 0.82f, ly), color, 1f);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Size textSize = TextMeasurer.Current.MeasureText(Value.ToString(Format), EffectiveFont);
        var content = new Size(textSize.Width + 18 + Padding.Horizontal, textSize.Height + Padding.Vertical + 6);
        return ResolveSize(content, availableSize);
    }

    protected override void OnClick(MouseClickEventArgs e)
    {
        Form? owner = FindOwner();
        if (owner is null) return;

        e.Handled = true;

        _calendar = new Calendar();
        _calendar.SetSelectedDate(Value);
        _calendar.DateSelected += OnDateSelected;

        owner.ShowFlyout(this, _calendar, FlyoutPlacement.Bottom);
    }

    private void OnDateSelected(object? sender, DateTime date)
    {
        SetValue(date);

        if (_calendar is not null)
        {
            FindOwner()?.CloseFlyout(_calendar);
            _calendar.DateSelected -= OnDateSelected;
            _calendar = null;
        }
    }
}
