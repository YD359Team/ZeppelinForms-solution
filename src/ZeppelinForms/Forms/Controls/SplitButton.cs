using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Helpers;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Layout;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

public partial class SplitButton : ButtonBase
{
    private const float ArrowZoneWidth = 26f;

    private readonly FlyoutHost _flyout;
    private bool _arrowHovered;
    private MenuItem? _lastInvoked;

    public string? Text { get; set; }
    public List<MenuItem> Items { get; init; } = [];

    /// <summary>Нажатие на основную часть повторяет последний выбранный пункт.</summary>
    public bool RepeatLastAction { get; set; } = true;

    [Styled(Category = "Button")]
    public partial Color SeparatorColor { get; set; }
    private static Color SeparatorColorDefault => new(120, 255, 255, 255);

    public bool IsMenuOpen => _flyout.IsOpen;

    public SplitButton()
    {
        SetControlDefault(BackgroundProperty, new Color(255, 0x0D, 0x6E, 0xFD));
        HoverBackgroundColor = new Color(255, 0x0B, 0x5E, 0xD7);
        PressedBackgroundColor = new Color(255, 0x0A, 0x53, 0xBE);
        SetControlDefault(TextColorProperty, Colors.White);
        SetControlDefault(BorderColorProperty, new Color(255, 0x0D, 0x6E, 0xFD));

        // волна на составной кнопке сбивает с толку: непонятно,
        // сработала основная часть или стрелка
        RippleEnabled = false;

        _flyout = new FlyoutHost(this);
        _flyout.Closed += (_, _) => InvalidateVisual();
    }

    private Rectangle ArrowZone => new(
        new Point(ActualSize.Width - ArrowZoneWidth, 0),
        new Size(ArrowZoneWidth, ActualSize.Height));

    /// <summary>Наведение на стрелку не должно подсвечивать всю кнопку —
    /// подложка остаётся обычной, а зона стрелки красится отдельно.</summary>
    protected override Color CurrentBackground =>
        _arrowHovered && IsEnabled ? BackgroundColor : base.CurrentBackground;

    protected override void DrawButtonContent(Graphics g)
    {
        if (_arrowHovered || _flyout.IsOpen)
            g.FillRectangle(ArrowZone, HoverBackgroundColor);

        // разделитель между основной частью и стрелкой
        float separatorX = ActualSize.Width - ArrowZoneWidth;

        g.DrawLine(
            new Point(separatorX, 4f),
            new Point(separatorX, ActualSize.Height - 4f),
            SeparatorColor, 1f);

        if (!string.IsNullOrEmpty(Text))
        {
            var textRect = new Rectangle(
                new Point(Padding.Left, Padding.Top),
                new Size(
                    Math.Max(0, ActualSize.Width - ArrowZoneWidth - Padding.Horizontal),
                    Math.Max(0, ActualSize.Height - Padding.Vertical)));

            g.DrawText(Text, textRect, CurrentTextColor, EffectiveFont,
                HorizontalContentAlignment.Center, VerticalContentAlignment.Center);
        }

        Rectangle arrow = ArrowZone;
        float cx = arrow.X + arrow.Width / 2f;
        float cy = arrow.Y + arrow.Height / 2f;

        ReadOnlySpan<Point> triangle = _flyout.IsOpen
            ? [new(cx - 4f, cy + 2f), new(cx, cy - 2.5f), new(cx + 4f, cy + 2f)]
            : [new(cx - 4f, cy - 2f), new(cx, cy + 2.5f), new(cx + 4f, cy - 2f)];

        g.DrawPolyline(triangle, CurrentTextColor, 1.6f);
    }

    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        float localX = e.Location.X - GetAbsolutePosition().X;
        bool inArrow = localX >= ActualSize.Width - ArrowZoneWidth;

        if (inArrow == _arrowHovered) return;

        _arrowHovered = inArrow;
        InvalidateVisual();
    }

    protected override void OnMouseExit(MouseMoveEventArgs e)
    {
        _arrowHovered = false;
        InvalidateVisual();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        // волна отключена, но базовый обработчик всё равно вызываем:
        // он отвечает и за состояние нажатия
        base.OnMouseDown(e);
    }

    protected override void OnClick(MouseClickEventArgs e)
    {
        e.Handled = true;

        if (!IsEnabled) return;

        float localX = e.Location.X - GetAbsolutePosition().X;

        if (localX >= ActualSize.Width - ArrowZoneWidth)
        {
            if (Items.Count > 0)
                _flyout.Toggle(BuildMenu);

            InvalidateVisual();
            return;
        }

        OnActivated();
    }

    protected override void OnActivated()
    {
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