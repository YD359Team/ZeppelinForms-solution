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

    private ListBox? _dropDown;
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
            Invalidate();
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

    protected override bool IsKeyActivatable => true;

    public ComboBox()
    {
        Background = Colors.White;
        Padding = new Thickness(6, 3);
    }

    private string TextOf(object item) => DisplaySelector?.Invoke(item) ?? item?.ToString() ?? string.Empty;

    public override void Draw(Graphics g)
    {
        if (Background.A > 0)
            g.FillRectangle(this.LocalBounds, Background);

        if (BorderWidth > 0)
            g.DrawRectangle(this.LocalBounds, IsFocused ? LightThemeColors.ButtonFill : BorderColor, BorderWidth);

        var content = this.ContentBounds;

        var textArea = new Rectangle(
            content.AsPosition(),
            new Size(Math.Max(0, content.Width - ArrowWidth), content.Height));

        if (SelectedItem is object item)
            g.DrawText(TextOf(item), textArea, TextColor, EffectiveFont,
                HorizontalContentAlignment.Left, VerticalContentAlignment.Center);
        else if (!string.IsNullOrEmpty(PlaceholderText))
            g.DrawText(PlaceholderText, textArea, PlaceholderColor, EffectiveFont,
                HorizontalContentAlignment.Left, VerticalContentAlignment.Center);

        // стрелка вниз
        float cx = content.X + content.Width - ArrowWidth / 2f;
        float cy = content.Y + content.Height / 2f;

        ReadOnlySpan<Point> arrow =
        [
            new(cx - 4.5f, cy - 2f),
            new(cx, cy + 3f),
            new(cx + 4.5f, cy - 2f),
        ];

        g.DrawPolyline(arrow, TextColor, 1.6f);
    }

    protected override void OnClick(MouseClickEventArgs e)
    {
        e.Handled = true;

        if (_dropDown is not null)
        {
            CloseDropDown();
            return;
        }

        OpenDropDown();
    }

    private void OpenDropDown()
    {
        Form? owner = FindOwner();
        if (owner is null || Items.Count == 0) return;

        var list = new ListBox
        {
            // ширина как у самого комбобокса — так выпадающий список
            // выглядит его продолжением, а не отдельным окном
            Size = new Size(Size.Width, DropDownHeight),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        foreach (object item in Items)
            list.Items.Add(TextOf(item));

        list.SelectedIndex = _selectedIndex;

        list.SelectionChanged += (_, _) =>
        {
            SelectedIndex = list.SelectedIndex;
            CloseDropDown();
        };

        _dropDown = list;
        owner.ShowFlyout(this, list, FlyoutPlacement.Bottom);
    }

    private void CloseDropDown()
    {
        if (_dropDown is null) return;

        FindOwner()?.CloseFlyout(_dropDown);
        _dropDown = null;
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

            case Key.Escape when _dropDown is not null:
                CloseDropDown();
                e.Handled = true;
                break;

            default:
                base.OnKeyDown(e);   // пробел/Enter раскроют список
                break;
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (_dropDown is not null) return;   // прокрутка внутри списка важнее

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
