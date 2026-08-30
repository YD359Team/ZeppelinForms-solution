using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Helpers;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Layout;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

public class SplitButton : UnitControl, IInputElement, IBorderedElement
{
    private const float ArrowZoneWidth = 26f;

    private UIElement? _flyout;
    private bool _arrowHovered;

    public string? Text { get; set; }
    public List<MenuItem> Items { get; init; } = [];

    /// <summary>Нажатие на основную часть повторяет последний выбранный пункт.</summary>
    public bool RepeatLastAction { get; set; } = true;

    private MenuItem? _lastInvoked;

    public Color BackgroundColor { get; set; } = LightThemeColors.ButtonFill;
    public Color HoverBackgroundColor { get; set; } = LightThemeColors.ButtonFill.Darken();
    public Color TextColor { get; set; } = Colors.White;

    public Color BorderColor { get; set; } = LightThemeColors.ButtonFill;
    public float BorderWidth { get; set; } = 1f;

    public bool IsFocused { get; set; }
    public bool TabStop { get; set; } = true;
    public uint TabIndex { get; set; }

    protected override bool IsKeyActivatable => true;

    public SplitButton()
    {
        Padding = new Thickness(14, 6);
        CornerRadius = new CornerRadius(4f);
    }

    private Rectangle ArrowZone => new(
        new Point(ActualSize.Width - ArrowZoneWidth, 0),
        new Size(ArrowZoneWidth, ActualSize.Height));

    public override void Draw(Graphics g)
    {
        var bounds = this.LocalBounds;

        g.FillRoundRectangle(bounds, CornerRadius,
            IsHovered && !_arrowHovered ? HoverBackgroundColor : BackgroundColor);

        if (_arrowHovered)
            g.FillRectangle(ArrowZone, HoverBackgroundColor);

        // разделитель между основной частью и стрелкой
        float sepX = ActualSize.Width - ArrowZoneWidth;
        g.DrawLine(
            new Point(sepX, 4f),
            new Point(sepX, ActualSize.Height - 4f),
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

        ReadOnlySpan<Point> triangle =
        [
            new(cx - 4f, cy - 2f),
            new(cx, cy + 2.5f),
            new(cx + 4f, cy - 2f),
        ];

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
            OpenMenu();
            return;
        }

        if (RepeatLastAction && _lastInvoked is not null)
            _lastInvoked.RaiseClick();
    }

    private void OpenMenu()
    {
        Form? owner = FindOwner();
        if (owner is null || Items.Count == 0) return;

        if (_flyout is not null)
        {
            owner.CloseFlyout(_flyout);
            _flyout = null;
            return;
        }

        var menu = new MenuList { Items = Items };

        menu.ItemInvoked += (_, item) =>
        {
            _lastInvoked = item;
            owner.CloseAllFlyouts();
            _flyout = null;
            InvalidateVisual();
        };

        _flyout = menu;
        owner.ShowFlyout(this, menu, FlyoutPlacement.Bottom);
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