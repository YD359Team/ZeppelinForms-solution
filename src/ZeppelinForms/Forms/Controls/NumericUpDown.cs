using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

public class NumericUpDown : UnitControl, IInputElement, IBorderedElement
{
    private const float ButtonWidth = 18f;

    private decimal _value;
    private bool _hoverUp;
    private bool _hoverDown;

    public decimal Minimum { get; set; } = 0;
    public decimal Maximum { get; set; } = 100;
    public decimal Step { get; set; } = 1;
    public int DecimalPlaces { get; set; }

    public decimal Value
    {
        get => _value;
        set
        {
            decimal clamped = Math.Clamp(value, Minimum, Maximum);
            if (_value == clamped) return;

            _value = clamped;
            ValueChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    public event EventHandler? ValueChanged;

    public Color TextColor { get; set; } = Colors.Black;
    public Color ButtonColor { get; set; } = new Color(255, 240, 240, 240);
    public Color ButtonHoverColor { get; set; } = new Color(255, 220, 220, 220);

    public Color BorderColor { get; set; } = Colors.Black;
    public float BorderWidth { get; set; } = 1f;

    public bool IsFocused { get; set; }
    public bool TabStop { get; set; } = true;
    public uint TabIndex { get; set; }

    public NumericUpDown()
    {
        Background = Colors.White;
        Padding = new Thickness(6, 3);
    }

    private string DisplayText => _value.ToString($"F{DecimalPlaces}");

    private Rectangle UpButtonRect => new(
        new Point(ActualSize.Width - ButtonWidth, 0),
        new Size(ButtonWidth, ActualSize.Height / 2f));

    private Rectangle DownButtonRect => new(
        new Point(ActualSize.Width - ButtonWidth, ActualSize.Height / 2f),
        new Size(ButtonWidth, ActualSize.Height / 2f));

    public override void Draw(Graphics g)
    {
        if (Background.A > 0)
            g.FillRectangle(this.LocalBounds, Background);

        var text = new Rectangle(
            new Point(Padding.Left, Padding.Top),
            new Size(
                Math.Max(0, ActualSize.Width - ButtonWidth - Padding.Horizontal),
                Math.Max(0, ActualSize.Height - Padding.Vertical)));

        g.DrawText(DisplayText, text, TextColor, EffectiveFont,
            HorizontalContentAlignment.Right, VerticalContentAlignment.Center);

        g.FillRectangle(UpButtonRect, _hoverUp ? ButtonHoverColor : ButtonColor);
        g.FillRectangle(DownButtonRect, _hoverDown ? ButtonHoverColor : ButtonColor);

        DrawArrow(g, UpButtonRect, up: true);
        DrawArrow(g, DownButtonRect, up: false);

        if (BorderWidth > 0)
            g.DrawRectangle(this.LocalBounds, IsFocused ? LightThemeColors.ButtonFill : BorderColor, BorderWidth);
    }

    private static void DrawArrow(Graphics g, Rectangle area, bool up)
    {
        float cx = area.X + area.Width / 2f;
        float cy = area.Y + area.Height / 2f;
        float w = area.Width * 0.28f;
        float h = area.Height * 0.16f;

        ReadOnlySpan<Point> points = up
            ? [new(cx - w, cy + h), new(cx, cy - h), new(cx + w, cy + h)]
            : [new(cx - w, cy - h), new(cx, cy + h), new(cx + w, cy - h)];

        g.DrawPolyline(points, Colors.Black, 1.5f);
    }

    protected override void OnMouseMove(Point location)
    {
        Point abs = GetAbsolutePosition();
        var local = new Point(location.X - abs.X, location.Y - abs.Y);

        bool up = Contains(UpButtonRect, local);
        bool down = Contains(DownButtonRect, local);

        if (up != _hoverUp || down != _hoverDown)
        {
            _hoverUp = up;
            _hoverDown = down;
            Invalidate();
        }
    }

    protected override void OnMouseLeave()
    {
        _hoverUp = _hoverDown = false;
    }

    protected override void OnClick(MouseClickEventArgs e)
    {
        Point abs = GetAbsolutePosition();
        var local = new Point(e.Location.X - abs.X, e.Location.Y - abs.Y);

        if (Contains(UpButtonRect, local)) Value += Step;
        else if (Contains(DownButtonRect, local)) Value -= Step;
        else return;

        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        Value += Step * (e.Delta / 120);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up: Value += Step; e.Handled = true; break;
            case Key.Down: Value -= Step; e.Handled = true; break;
            case Key.Home: Value = Minimum; e.Handled = true; break;
            case Key.End: Value = Maximum; e.Handled = true; break;
        }
    }

    private static bool Contains(Rectangle rect, Point p) =>
        p.X >= rect.X && p.X <= rect.X + rect.Width &&
        p.Y >= rect.Y && p.Y <= rect.Y + rect.Height;

    protected override Size MeasureOverride(Size availableSize)
    {
        // меряем по самому длинному из граничных значений, чтобы поле
        // не прыгало по ширине при переборе значений
        string widest = Minimum.ToString($"F{DecimalPlaces}").Length >= Maximum.ToString($"F{DecimalPlaces}").Length
            ? Minimum.ToString($"F{DecimalPlaces}")
            : Maximum.ToString($"F{DecimalPlaces}");

        Size textSize = TextMeasurer.Current.MeasureText(widest, EffectiveFont);

        var content = new Size(
            textSize.Width + ButtonWidth + Padding.Horizontal + 8,
            textSize.Height + Padding.Vertical + 6);

        return ResolveSize(content, availableSize);
    }
}
