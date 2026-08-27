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
        var textArea = new Rectangle(content.AsPosition(), new Size(Math.Max(0, content.Width - 18), content.Height));

        g.DrawText(Value.ToString(Format), textArea, TextColor, EffectiveFont,
            HorizontalAlign.Left, VerticalAlign.Center);

        // стрелка-указатель справа
        var arrowArea = new Rectangle(
            new Point(content.X + content.Width - 18, content.Y), new Size(18, content.Height));

        g.DrawText("▾", arrowArea, TextColor, EffectiveFont,
            HorizontalAlign.Center, VerticalAlign.Center);
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