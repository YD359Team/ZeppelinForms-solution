using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Helpers;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Layout;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

public class SplitButton : UnitControl, IInputElement, IBorderedElement
{
    private const float ArrowZoneWidth = 26f;

    private readonly FlyoutHost _flyout;
    private bool _arrowHovered;
    private MenuItem? _lastInvoked;

    public string? Text { get; set; }
    public List<MenuItem> Items { get; init; } = [];

    /// <summary>Нажатие на основную часть повторяет последний выбранный пункт.</summary>
    public bool RepeatLastAction { get; set; } = true;

    public Color BackgroundColor { get; set; } = new Color(255, 0x0D, 0x6E, 0xFD);
    public Color HoverBackgroundColor { get; set; } = new Color(255, 0x0B, 0x5E, 0xD7);
    public Color TextColor { get; set; } = Colors.White;

    public Color BorderColor { get; set; } = new Color(255, 0x0D, 0x6E, 0xFD);
    public float BorderWidth { get; set; } = 1f;

    public bool IsFocused { get; set; }
    public bool TabStop { get; set; } = true;
    public uint TabIndex { get; set; }

    public bool IsMenuOpen => _flyout.IsOpen;

    protected override bool IsKeyActivatable => true;

    public SplitButton()
    {
        Padding = new Thickness(14, 6);
        CornerRadius = new CornerRadius(4f);
        Cursor = CursorKind.Hand;

        _flyout = new FlyoutHost(this);
        _flyout.Closed += (_, _) => InvalidateVisual();
    }

    private Rectangle ArrowZone => new(
        new Point(ActualSize.Width - ArrowZoneWidth, 0),
        new Size(ArrowZoneWidth, ActualSize.Height));

    public override void Draw(Graphics g)
    {
        var bounds = this.LocalBounds;

        g.FillRoundRectangle(bounds, CornerRadius,
            IsHovered && !_arrowHovered ? HoverBackgroundColor : BackgroundColor);

        if (_arrowHovered || _flyout.IsOpen)
            g.FillRectangle(ArrowZone, HoverBackgroundColor);

        // разделитель между основной частью и стрелкой
        float separatorX = ActualSize.Width - ArrowZoneWidth;

        g.DrawLine(
            new Point(separatorX, 4f),
            new Point(separatorX, ActualSize.Height - 4f),
            new Color(120, 255, 255, 255), 1f);

        if (!string.IsNullOrEmpty(Text))
        {
            var textRect = new Rectangle(
                new Point(Padding.Left, Padding.Top),
                new Size(
                    Math.Max(0, ActualSize.Width - ArrowZoneWidth - Padding.Horizontal),
                    Math.Max(0, ActualSize.Height - Padding.Vertical)));

            g.DrawText(Text, textRect, TextColor, EffectiveFont,
                HorizontalContentAlignment.Center, VerticalContentAlignment.Center);
        }

        Rectangle arrow = ArrowZone;
        float cx = arrow.X + arrow.Width / 2f;
        float cy = arrow.Y + arrow.Height / 2f;

        ReadOnlySpan<Point> triangle = _flyout.IsOpen
            ? [new(cx - 4f, cy + 2f), new(cx, cy - 2.5f), new(cx + 4f, cy + 2f)]
            : [new(cx - 4f, cy - 2f), new(cx, cy + 2.5f), new(cx + 4f, cy - 2f)];

        g.DrawPolyline(triangle, TextColor, 1.6f);

        if (BorderWidth > 0)
            g.DrawRoundRectangle(bounds, CornerRadius, BorderColor, BorderWidth);
    }

    protected override void OnMouseMove(Point location)
    {
        float localX = location.X - GetAbsolutePosition().X;
        bool inArrow = localX >= ActualSize.Width - ArrowZoneWidth;

        if (inArrow == _arrowHovered) return;

        _arrowHovered = inArrow;
        InvalidateVisual();
    }

    protected override void OnMouseLeave() => _arrowHovered = false;

    protected override void OnClick(MouseClickEventArgs e)
    {
        e.Handled = true;

        float localX = e.Location.X - GetAbsolutePosition().X;

        if (localX >= ActualSize.Width - ArrowZoneWidth)
        {
            if (Items.Count > 0)
                _flyout.Toggle(BuildMenu);

            InvalidateVisual();
            return;
        }

        if (RepeatLastAction && _lastInvoked is not null)
            _lastInvoked.RaiseClick();
    }

    private UIElement BuildMenu()
    {
        var menu = new MenuList { Items = Items };

        menu.ItemInvoked += (_, item) =>
        {
            _lastInvoked = item;
            _flyout.Close();
        };

        return menu;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape when _flyout.IsOpen:
                _flyout.Close();
                e.Handled = true;
                break;

            case Key.Down when Items.Count > 0 && !_flyout.IsOpen:
                _flyout.Open(BuildMenu());
                InvalidateVisual();
                e.Handled = true;
                break;

            default:
                base.OnKeyDown(e);
                break;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Size textSize = string.IsNullOrEmpty(Text)
            ? Size.Empty
            : TextMeasurer.Current.MeasureText(Text, EffectiveFont);

        return ResolveSize(
            new Size(
                textSize.Width + ArrowZoneWidth + Padding.Horizontal,
                textSize.Height + Padding.Vertical),
            availableSize);
    }
}