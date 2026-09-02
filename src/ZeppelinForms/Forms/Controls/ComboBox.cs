using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Layout;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

public class ComboBox : UnitControl, IInputElement, IBorderedElement
{
    private const float ArrowWidth = 20f;

    private readonly FlyoutHost _flyout;
    private int _selectedIndex = -1;

    public List<object> Items { get; init; } = [];

    /// <summary>Как показать элемент. По умолчанию ToString().</summary>
    public Func<object, string>? DisplaySelector { get; set; }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            int clamped = value < 0 || value >= Items.Count ? -1 : value;
            if (_selectedIndex == clamped) return;

            _selectedIndex = clamped;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
    }

    public object? SelectedItem
    {
        get => _selectedIndex >= 0 ? Items[_selectedIndex] : null;
        set => SelectedIndex = value is null ? -1 : Items.IndexOf(value);
    }

    public event EventHandler? SelectionChanged;

    public string? PlaceholderText { get; set; }
    public float DropDownHeight { get; set; } = 180f;

    public Color TextColor { get; set; } = Colors.Black;
    public Color PlaceholderColor { get; set; } = new Color(255, 160, 160, 160);
    public Color BorderColor { get; set; } = Colors.Black;
    public float BorderWidth { get; set; } = 1f;

    public bool IsFocused { get; set; }
    public bool TabStop { get; set; } = true;
    public uint TabIndex { get; set; }

    public bool IsDropDownOpen => _flyout.IsOpen;

    protected override bool IsKeyActivatable => true;

    public ComboBox()
    {
        Background = Colors.White;
        Padding = new Thickness(6, 3);
        Cursor = CursorKind.Hand;

        _flyout = new FlyoutHost(this);
        _flyout.Closed += (_, _) => InvalidateVisual();
    }

    private string TextOf(object item) => DisplaySelector?.Invoke(item) ?? item?.ToString() ?? string.Empty;

    public override void Draw(Graphics g)
    {
        if (Background.A > 0)
            g.FillRoundRectangle(this.LocalBounds, CornerRadius, Background);

        if (BorderWidth > 0)
            g.DrawRoundRectangle(this.LocalBounds, CornerRadius,
                IsFocused ? App.Theme.Colors.BorderFocused : BorderColor, BorderWidth);

        var content = this.ContentBounds;

        var textArea = new Rectangle(
            content.Position,
            new Size(Math.Max(0, content.Width - ArrowWidth), content.Height));

        if (SelectedItem is object item)
            g.DrawText(TextOf(item), textArea, TextColor, EffectiveFont,
                HorizontalContentAlignment.Left, VerticalContentAlignment.Center);
        else if (!string.IsNullOrEmpty(PlaceholderText))
            g.DrawText(PlaceholderText, textArea, PlaceholderColor, EffectiveFont,
                HorizontalContentAlignment.Left, VerticalContentAlignment.Center);

        float cx = content.X + content.Width - ArrowWidth / 2f;
        float cy = content.Y + content.Height / 2f;

        // стрелка переворачивается, когда список раскрыт
        ReadOnlySpan<Point> arrow = _flyout.IsOpen
            ? [new(cx - 4.5f, cy + 2f), new(cx, cy - 3f), new(cx + 4.5f, cy + 2f)]
            : [new(cx - 4.5f, cy - 2f), new(cx, cy + 3f), new(cx + 4.5f, cy - 2f)];

        g.DrawPolyline(arrow, TextColor, 1.6f);
    }

    protected override void OnClick(MouseClickEventArgs e)
    {
        e.Handled = true;

        if (Items.Count == 0) return;

        _flyout.Toggle(BuildDropDown);
        InvalidateVisual();
    }

    private UIElement BuildDropDown()
    {
        var list = new ListBox
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            OverflowY = Overflow.Auto,
        };

        foreach (object item in Items)
            list.Items.Add(TextOf(item));

        list.SelectedIndex = _selectedIndex;

        list.SelectionChanged += (_, _) =>
        {
            SelectedIndex = list.SelectedIndex;
            _flyout.Close();
        };

        // сначала узнаём, сколько списку нужно, и только потом ограничиваем:
        // при трёх элементах не должно оставаться пустого места
        list.Measure(new Size(ActualSize.Width, float.PositiveInfinity));

        float height = Math.Min(list.DesiredSize.Height, DropDownHeight);

        list.Size = new Size(ActualSize.Width, height);

        return list;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up:
                SelectedIndex = Math.Max(0, _selectedIndex - 1);
                e.Handled = true;
                break;

            case Key.Down:
                SelectedIndex = Math.Min(Items.Count - 1, _selectedIndex + 1);
                e.Handled = true;
                break;

            case Key.Escape when _flyout.IsOpen:
                _flyout.Close();
                e.Handled = true;
                break;

            default:
                base.OnKeyDown(e);   // пробел/Enter раскроют список
                break;
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (_flyout.IsOpen) return;   // прокрутка внутри списка важнее

        SelectedIndex = Math.Clamp(_selectedIndex - Math.Sign(e.Delta), 0, Items.Count - 1);
        e.Handled = true;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        float widest = 0;

        foreach (object item in Items)
            widest = Math.Max(widest, TextMeasurer.Current.MeasureText(TextOf(item), EffectiveFont).Width);

        if (!string.IsNullOrEmpty(PlaceholderText))
            widest = Math.Max(widest, TextMeasurer.Current.MeasureText(PlaceholderText, EffectiveFont).Width);

        Size probe = TextMeasurer.Current.MeasureText("Wg", EffectiveFont);

        return ResolveSize(
            new Size(widest + ArrowWidth + Padding.Horizontal + 8, probe.Height + Padding.Vertical + 6),
            availableSize);
    }
}
