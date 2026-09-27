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

    public string? Text
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            // the button's width is computed from its caption
            Invalidate();
        }
    }

    public List<MenuItem> Items { get; init; } = [];

    /// <summary>Pressing the main part repeats the last chosen item.</summary>
    public bool RepeatLastAction { get; set; } = true;

    [Styled(Category = "Button")]
    public partial Color SeparatorColor { get; set; }
    private static Color SeparatorColorDefault => new(120, 255, 255, 255);

    /// <summary>Replace the caption with the chosen item. Off — the button
    /// always shows Text, like a regular button with a menu.</summary>
    [Styled(Category = "Button", AffectsLayout = true)]
    public partial bool ShowSelectedItem { get; set; }

    private static bool ShowSelectedItemDefault => true;

    /// <summary>Keep the width by the longest menu item rather than by the
    /// current caption. Otherwise the button jumps on every choice — that is
    /// the main nuisance of the text-replacement approach.</summary>
    [Styled(Category = "Button", AffectsLayout = true)]
    public partial bool StableWidth { get; set; }

    private static bool StableWidthDefault => true;

    /// <summary>The last chosen item. <see cref="RepeatLastAction"/> repeats it,
    /// and its caption is visible on the button.</summary>
    public MenuItem? SelectedItem
    {
        get => _lastInvoked;
        set
        {
            if (ReferenceEquals(_lastInvoked, value)) return;

            _lastInvoked = value;

            SelectionChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    public event EventHandler? SelectionChanged;

    /// <summary>What is actually written on the button.</summary>
    private string? DisplayText =>
        ShowSelectedItem && _lastInvoked?.Text is { Length: > 0 } header ? header : Text;

    public bool IsMenuOpen => _flyout.IsOpen;

    public SplitButton()
    {
        SetControlDefault(BackgroundProperty, new Color(255, 0x0D, 0x6E, 0xFD));
        SetControlDefault(HoverBackgroundColorProperty, new(255, 0x0B, 0x5E, 0xD7));
        SetControlDefault(PressedBackgroundColorProperty, new(255, 0x0A, 0x53, 0xBE));
        SetControlDefault(TextColorProperty, Colors.White);
        SetControlDefault(BorderColorProperty, new Color(255, 0x0D, 0x6E, 0xFD));

        // a ripple on a compound button is confusing:
        // it is unclear whether the main part or the arrow fired
        SetControlDefault(RippleEnabledProperty, false);

        _flyout = new FlyoutHost(this);
        _flyout.Closed += (_, _) => InvalidateVisual();
    }

    private Rectangle ArrowZone => new(
        new Point(ActualSize.Width - ArrowZoneWidth, 0),
        new Size(ArrowZoneWidth, ActualSize.Height));

    /// <summary>Hovering the arrow must not highlight the whole button —
    /// the backdrop stays normal, and the arrow zone is painted separately.</summary>
    protected override Color CurrentBackground =>
        _arrowHovered && IsEnabled ? BackgroundColor : base.CurrentBackground;

    protected override void DrawButtonContent(Graphics g)
    {
        if (_arrowHovered || _flyout.IsOpen)
            g.FillRectangle(ArrowZone, HoverBackgroundColor);

        // the separator between the main part and the arrow
        float separatorX = ActualSize.Width - ArrowZoneWidth;

        g.DrawLine(
            new Point(separatorX, 4f),
            new Point(separatorX, ActualSize.Height - 4f),
            SeparatorColor, 1f);

        if (DisplayText is { Length: > 0 } caption)
        {
            var textRect = new Rectangle(
                new Point(Padding.Left, Padding.Top),
                new Size(
                    Math.Max(0, ActualSize.Width - ArrowZoneWidth - Padding.Horizontal),
                    Math.Max(0, ActualSize.Height - Padding.Vertical)));

            g.DrawText(caption, textRect, CurrentTextColor, EffectiveFont,
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
        // the pressed state is set by UIElement.RaiseMouseDown before this hook;
        // the base only starts the ripple, which is off by default for this button
        // but can be turned on — so the base is still called
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
            SelectedItem = item;
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
        Size content = StableWidth ? WidestCaption() : Measure(DisplayText);

        return ResolveSize(
            new Size(
                content.Width + ArrowZoneWidth + Padding.Horizontal,
                content.Height + Padding.Vertical),
            availableSize);
    }

    private Size Measure(string? text) => string.IsNullOrEmpty(text)
        ? Size.Empty
        : TextMeasurer.Current.MeasureText(text, EffectiveFont);

    /// <summary>The widest of the possible captions: the initial one and all
    /// menu items. That way the button's width doesn't change on a choice.</summary>
    private Size WidestCaption()
    {
        Size widest = Measure(Text);

        if (!ShowSelectedItem) return widest;

        foreach (MenuItem item in Items)
        {
            Size size = Measure(item.Text);

            widest = new Size(
                Math.Max(widest.Width, size.Width),
                Math.Max(widest.Height, size.Height));
        }

        return widest;
    }
}